using Anvil.CSharp.Core;
using Anvil.Unity.DOTS.Data;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Anvil.Unity.DOTS.Entities
{
    public interface ITaskDriver
    {
        JobHandle Update(JobHandle dependsOn);
    }

    public abstract class AbstractTaskDriver<TTaskDriver, TKey, TSource, TResult> : AbstractAnvilBase,
                                                                                    ITaskDriver
        where TTaskDriver : AbstractTaskDriver<TTaskDriver, TKey, TSource, TResult>
        where TKey : struct, IEquatable<TKey>
        where TSource : struct, ILookupValue<TKey>
        where TResult : struct, ILookupValue<TKey>
    {
        public delegate JobHandle PopulateEntitiesFunction(JobHandle dependsOn,
                                                           JobEntitiesSourceWriter<TSource> addStruct,
                                                           JobResultWriter<TResult> completeStruct);

        public delegate JobHandle PopulateAsyncFunction(JobHandle dependsOn,
                                                        JobSourceWriter<TSource> addStruct,
                                                        JobResultWriter<TResult> completeStruct);

        public delegate JobHandle PopulateFunction(JobSourceMainThreadWriter<TSource> addStruct,
                                                   JobResultWriter<TResult> completeStruct);

        private readonly List<ITaskDriver> m_ChildTaskDrivers = new List<ITaskDriver>();

        private readonly VirtualData<TKey, TSource> m_SourceData;
        private readonly VirtualData<TKey, TResult> m_ResultData;
        private readonly PopulateEntitiesFunction m_PopulateEntitiesFunction;

        protected World World
        {
            get;
        }

        protected AbstractTaskDriverSystem System
        {
            get;
        }

        protected AbstractTaskDriver(AbstractTaskDriverSystem owningSystem,
                                     VirtualData<TKey, TSource> sourceData,
                                     PopulateEntitiesFunction populateEntitiesFunction)
        {
            System = owningSystem;
            World = owningSystem.World;
            m_SourceData = sourceData;
            m_PopulateEntitiesFunction = populateEntitiesFunction;
            //TODO: How to specify?
            m_ResultData = new VirtualData<TKey, TResult>(BatchStrategy.MaximizeChunk, sourceData);
        }

        protected override void DisposeSelf()
        {
            m_ResultData.Dispose();

            base.DisposeSelf();
        }

        protected void AddChildTaskDriver<TChildTaskDriverSystem, TChildTaskDriver, TChildKey, TChildSource, TChildResult>(AbstractTaskDriver<TChildTaskDriver, TChildKey, TChildSource, TChildResult>.PopulateEntitiesFunction populateEntitiesFunction)
            where TChildTaskDriverSystem : AbstractTaskDriverSystem<TChildTaskDriverSystem, TChildTaskDriver, TChildKey, TChildSource, TChildResult>
            where TChildTaskDriver : AbstractTaskDriver<TChildTaskDriver, TChildKey, TChildSource, TChildResult>
            where TChildKey : struct, IEquatable<TChildKey>
            where TChildSource : struct, ILookupValue<TChildKey>
            where TChildResult : struct, ILookupValue<TChildKey>
        {
            TChildTaskDriverSystem system = World.GetOrCreateSystem<TChildTaskDriverSystem>();
            system.OnCreated += (driverSystem) =>
                                {
                                    driverSystem.CreateTaskDriver(populateEntitiesFunction);
                                };
        }

        public JobHandle Update(JobHandle dependsOn)
        {
            JobHandle addHandle = m_SourceData.AcquireForEntitiesAddAsync(out JobEntitiesSourceWriter<TSource> addStruct);
            JobResultWriter<TResult> resultStruct = m_ResultData.GetCompletionWriter();

            JobHandle prePopulate = JobHandle.CombineDependencies(addHandle, dependsOn);

            JobHandle postPopulate = m_PopulateEntitiesFunction(prePopulate, addStruct, resultStruct);

            m_SourceData.ReleaseForEntitiesAddAsync(postPopulate);

            JobHandle consolidateResultHandle = m_ResultData.ConsolidateForFrame(postPopulate);

            //TODO: Could add a hook for user processing before all children go

            JobHandle childrenUpdateHandle = UpdateChildren(consolidateResultHandle);

            //TODO: Could add a hook for user processing after all children go

            return childrenUpdateHandle;
        }

        private JobHandle UpdateChildren(JobHandle dependsOn)
        {
            int numChildren = m_ChildTaskDrivers.Count;
            if (numChildren == 0)
            {
                return dependsOn;
            }

            NativeArray<JobHandle> childDependencies = new NativeArray<JobHandle>(m_ChildTaskDrivers.Count, Allocator.Temp);
            for (int i = 0; i < m_ChildTaskDrivers.Count; ++i)
            {
                childDependencies[i] = m_ChildTaskDrivers[i].Update(dependsOn);
            }

            return JobHandle.CombineDependencies(childDependencies);
        }
    }
}
