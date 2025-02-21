using Anvil.CSharp.Collections;
using Anvil.CSharp.Core;
using Anvil.CSharp.Reflection;
using Anvil.Unity.DOTS.Jobs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Anvil.CSharp.Logging;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    internal abstract class AbstractJobConfig : AbstractAnvilBase,
                                                IJobConfigRequirements
    {
        internal enum Usage
        {
            /// <summary>
            /// The data is being written to exclusively.
            /// Represents an Exclusive Write lock on the underlying data.
            /// </summary>
            ExclusiveWrite,

            /// <summary>
            /// The data is being Updated. It will either continue to be processed again the next frame or be
            /// resolved into a resolve target <see cref="TaskStream{TInstance}"/>
            /// Represents an Exclusive Write lock on the underlying data.
            /// </summary>
            //Duplicating this but making them the same so that the context of the code and docs makes sense but
            //they mean the same thing
            Update = ExclusiveWrite,

            /// <summary>
            /// The data is being written to.
            /// Represents a Shared Write lock on the underlying data.
            /// </summary>
            Write,

            /// <summary>
            /// The data is being read from.
            /// Represents a Shared Read lock on the underlying data.
            /// </summary>
            Read,

            /// <summary>
            /// The special id data is being written to so specific instances can be cancelled.
            /// Represents a Shared Write lock on the underlying data.
            /// </summary>
            WritePendingCancel,

            /// <summary>
            /// The data is being Cancelled. It will either continue to be processed again the next frame or be
            /// resolved into a resolve target <see cref="TaskStream{TInstance}"/>
            /// Represents an Exclusive Write lock on the underlying data.
            /// Similar to <see cref="Update"/> but operates only on instances that have been cancelled.
            /// </summary>
            Cancelling,

            /// <summary>
            /// The data is being written to a resolve target <see cref="TaskStream{TInstance}"/>.
            /// Represents a Shared Write lock on the underlying data.
            /// </summary>
            Resolve
        }

        internal static readonly BulkScheduleDelegate<AbstractJobConfig> PREPARE_AND_SCHEDULE_FUNCTION = BulkSchedulingUtil.CreateSchedulingDelegate<AbstractJobConfig>(nameof(PrepareAndSchedule), BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Usage[] USAGE_TYPES = (Usage[])Enum.GetValues(typeof(Usage));

        private readonly string m_TypeString;
        private readonly Dictionary<JobConfigDataID, AbstractAccessWrapper> m_AccessWrappers;
        private readonly List<AbstractAccessWrapper> m_SchedulingAccessWrappers;

        private NativeArray<JobHandle> m_AccessWrapperDependencies;
        private AbstractScheduleInfo m_ScheduleInfo;
        private bool m_ShouldDisableAfterNextRun;
        private bool m_IsHardened;

        /// <inheritdoc cref="IJobConfig.IsEnabled"/>
        public bool IsEnabled
        {
            get;
            set;
        }

        protected TaskFlowGraph TaskFlowGraph { get; }
        protected internal AbstractTaskSystem TaskSystem { get; }
        protected internal AbstractTaskDriver TaskDriver { get; }


        protected AbstractJobConfig(TaskFlowGraph taskFlowGraph,
                                    AbstractTaskSystem taskSystem,
                                    AbstractTaskDriver taskDriver)
        {
            IsEnabled = true;
            TaskFlowGraph = taskFlowGraph;
            TaskSystem = taskSystem;
            TaskDriver = taskDriver;

            m_AccessWrappers = new Dictionary<JobConfigDataID, AbstractAccessWrapper>();
            m_SchedulingAccessWrappers = new List<AbstractAccessWrapper>();
        }

        internal void AssignScheduleInfo(AbstractScheduleInfo scheduleInfo)
        {
            Debug_EnsureNoScheduleInfo();
            m_ScheduleInfo = scheduleInfo;
        }

        protected override void DisposeSelf()
        {
            if (m_AccessWrapperDependencies.IsCreated)
            {
                m_AccessWrapperDependencies.Dispose();
            }

            m_AccessWrappers.DisposeAllValuesAndClear();

            base.DisposeSelf();
        }

        public override string ToString()
        {
            return $"{GetType().GetReadableName()} with schedule function name of {m_ScheduleInfo?.ScheduleJobFunctionInfo ?? "NOT YET SET"} on {TaskDebugUtil.GetLocationName(TaskSystem, TaskDriver)}";
        }

        //*************************************************************************************************************
        // CONFIGURATION - COMMON
        //*************************************************************************************************************

        /// <inheritdoc cref="IJobConfig.RunOnce"/>
        public IJobConfig RunOnce()
        {
            m_ShouldDisableAfterNextRun = true;
            return this;
        }

        protected void AddAccessWrapper(AbstractAccessWrapper accessWrapper)
        {
            Debug_EnsureWrapperValidity(accessWrapper.ID);
            Debug_EnsureWrapperUsage(accessWrapper);
            m_AccessWrappers.Add(accessWrapper.ID, accessWrapper);
        }

        //*************************************************************************************************************
        // CONFIGURATION - REQUIRED DATA - DATA STREAM
        //*************************************************************************************************************

        /// <inheritdoc cref="IJobConfigRequirements.RequireTaskStreamForWrite{TInstance}"/>
        public IJobConfigRequirements RequireTaskStreamForWrite<TInstance>(TaskStream<TInstance> taskStream)
            where TInstance : unmanaged, IEntityProxyInstance
        {
            return RequireDataStreamForWrite(taskStream.DataStream, Usage.Write);
        }

        /// <inheritdoc cref="IJobConfigRequirements.RequireTaskStreamForRead{TInstance}"/>
        public IJobConfigRequirements RequireTaskStreamForRead<TInstance>(TaskStream<TInstance> taskStream)
            where TInstance : unmanaged, IEntityProxyInstance
        {
            AddAccessWrapper(new DataStreamAccessWrapper<TInstance>(taskStream.DataStream, AccessType.SharedRead, Usage.Read));
            return this;
        }

        /// <inheritdoc cref="IJobConfigRequirements.RequireTaskDriverForRequestCancel"/>
        public IJobConfigRequirements RequireTaskDriverForRequestCancel(AbstractTaskDriver taskDriver)
        {
            CancelRequestsDataStream cancelRequestsDataStream = taskDriver.CancelRequestsDataStream;
            AddAccessWrapper(new CancelRequestsAccessWrapper(cancelRequestsDataStream, AccessType.SharedWrite, Usage.Write, taskDriver.Context));

            return this;
        }

        protected IJobConfigRequirements RequireDataStreamForWrite<TInstance>(EntityProxyDataStream<TInstance> dataStream, Usage usage)
            where TInstance : unmanaged, IEntityProxyInstance
        {
            AddAccessWrapper(new DataStreamAccessWrapper<TInstance>(dataStream, AccessType.SharedWrite, usage));
            return this;
        }

        //*************************************************************************************************************
        // CONFIGURATION - REQUIRED DATA - GENERIC DATA
        //*************************************************************************************************************

        /// <inheritdoc cref="IJobConfigRequirements.RequireDataForRead{TData}"/>
        public IJobConfigRequirements RequireDataForRead<TData>(AccessControlledValue<TData> collection)
            where TData : struct
        {
            AddAccessWrapper(new GenericDataAccessWrapper<TData>(collection, AccessType.SharedRead, Usage.Read));
            return this;
        }

        /// <inheritdoc cref="IJobConfigRequirements.RequireDataForWrite{TData}"/>
        public IJobConfigRequirements RequireDataForWrite<TData>(AccessControlledValue<TData> collection)
            where TData : struct
        {
            AddAccessWrapper(new GenericDataAccessWrapper<TData>(collection, AccessType.SharedWrite, Usage.Write));
            return this;
        }

        /// <inheritdoc cref="IJobConfigRequirements.RequireDataForExclusiveWrite{TData}"/>
        public IJobConfigRequirements RequireDataForExclusiveWrite<TData>(AccessControlledValue<TData> collection)
            where TData : struct
        {
            AddAccessWrapper(new GenericDataAccessWrapper<TData>(collection, AccessType.ExclusiveWrite, Usage.ExclusiveWrite));
            return this;
        }

        //*************************************************************************************************************
        // CONFIGURATION - REQUIRED DATA - ENTITY QUERY
        //*************************************************************************************************************

        /// <inheritdoc cref="IJobConfigRequirements.RequireEntityNativeArrayFromQueryForRead"/>
        public IJobConfigRequirements RequireEntityNativeArrayFromQueryForRead(EntityQuery entityQuery)
        {
            return RequireEntityNativeArrayFromQueryForRead(new EntityQueryNativeArray(entityQuery));
        }

        /// <inheritdoc cref="IJobConfigRequirements.RequireIComponentDataNativeArrayFromQueryForRead{T}"/>
        public IJobConfigRequirements RequireIComponentDataNativeArrayFromQueryForRead<T>(EntityQuery entityQuery)
            where T : struct, IComponentData
        {
            return RequireIComponentDataNativeArrayFromQueryForRead(new EntityQueryComponentNativeArray<T>(entityQuery));
        }

        protected IJobConfigRequirements RequireEntityNativeArrayFromQueryForRead(EntityQueryNativeArray entityQueryNativeArray)
        {
            AddAccessWrapper(new EntityQueryAccessWrapper(entityQueryNativeArray, Usage.Read));
            return this;
        }

        protected IJobConfigRequirements RequireIComponentDataNativeArrayFromQueryForRead<T>(EntityQueryComponentNativeArray<T> entityQueryNativeArray)
            where T : struct, IComponentData
        {
            AddAccessWrapper(new EntityQueryComponentAccessWrapper<T>(entityQueryNativeArray, Usage.Read));
            return this;
        }

        //*************************************************************************************************************
        // CONFIGURATION - REQUIRED DATA - ComponentDataFromEntity (CDFE)
        //*************************************************************************************************************

        //TODO: #86 - Revisit this section after Entities 1.0 upgrade for name changes to CDFE
        /// <inheritdoc cref="IJobConfigRequirements.RequireCDFEForRead{T}"/>
        public IJobConfigRequirements RequireCDFEForRead<T>()
            where T : struct, IComponentData
        {
            AddAccessWrapper(new CDFEAccessWrapper<T>(AccessType.SharedRead, Usage.Read, TaskSystem));
            return this;
        }

        /// <inheritdoc cref="IJobConfigRequirements.RequireCDFEForWrite{T}"/>
        public IJobConfigRequirements RequireCDFEForWrite<T>()
            where T : struct, IComponentData
        {
            AddAccessWrapper(new CDFEAccessWrapper<T>(AccessType.SharedWrite, Usage.Write, TaskSystem));
            return this;
        }

        //*************************************************************************************************************
        // CONFIGURATION - REQUIRED DATA - DynamicBuffer
        //*************************************************************************************************************

        /// <inheritdoc cref="IJobConfigRequirements.RequireDBFEForRead{T}"/>
        public IJobConfigRequirements RequireDBFEForRead<T>()
            where T : struct, IBufferElementData
        {
            AddAccessWrapper(new DynamicBufferAccessWrapper<T>(AccessType.SharedRead, Usage.Read, TaskSystem));

            return this;
        }

        /// <inheritdoc cref="IJobConfigRequirements.RequireDBFEForExclusiveWrite{T}"/>
        public IJobConfigRequirements RequireDBFEForExclusiveWrite<T>()
            where T : struct, IBufferElementData
        {
            AddAccessWrapper(new DynamicBufferAccessWrapper<T>(AccessType.ExclusiveWrite, Usage.ExclusiveWrite, TaskSystem));
            return this;
        }

        //*************************************************************************************************************
        // HARDEN
        //*************************************************************************************************************

        public void Harden()
        {
            //During Hardening we can optimize by pre-allocating native arrays for dependency combining and convert
            //dictionary iterations into lists. We also allow for sub classes to do their own optimizing if needed.

            Debug_EnsureNotHardened();
            m_IsHardened = true;

            foreach (AbstractAccessWrapper wrapper in m_AccessWrappers.Values)
            {
                m_SchedulingAccessWrappers.Add(wrapper);
            }

            m_AccessWrapperDependencies = new NativeArray<JobHandle>(m_SchedulingAccessWrappers.Count + 1, Allocator.Persistent);

            HardenConfig();
        }

        protected virtual void HardenConfig()
        {
        }

        //*************************************************************************************************************
        // EXECUTION
        //*************************************************************************************************************

        private JobHandle PrepareAndSchedule(JobHandle dependsOn)
        {
            //The main use for JobConfig's, this handles getting the dependency for every piece of data that the job
            //will read from or write to and combine them into one to actually schedule the job with Unity's job
            //system. The resulting handle from that job is then fed back to each piece of data to allow Unity's
            //dependency system to know when it's safe to use the data again.

            Debug_EnsureScheduleInfoExists();

            if (!IsEnabled)
            {
                return dependsOn;
            }

            if (m_ShouldDisableAfterNextRun)
            {
                IsEnabled = false;
            }

            Debug_EnsureIsHardened();

            int index = 0;
            for (; index < m_SchedulingAccessWrappers.Count; ++index)
            {
                m_AccessWrapperDependencies[index] = m_SchedulingAccessWrappers[index].Acquire();
            }

            m_AccessWrapperDependencies[index] = dependsOn;

            dependsOn = JobHandle.CombineDependencies(m_AccessWrapperDependencies);
            dependsOn = m_ScheduleInfo.CallScheduleFunction(dependsOn);

            foreach (AbstractAccessWrapper wrapper in m_SchedulingAccessWrappers)
            {
                wrapper.Release(dependsOn);
            }

            return dependsOn;
        }

        private TWrapper GetAccessWrapper<TWrapper>(Usage usage)
            where TWrapper : AbstractAccessWrapper
        {
            JobConfigDataID id = new JobConfigDataID(typeof(TWrapper), usage);
            Debug_EnsureWrapperExists(id);
            return (TWrapper)m_AccessWrappers[id];
        }

        internal EntityProxyDataStream<TInstance> GetDataStream<TInstance>(Usage usage)
            where TInstance : unmanaged, IEntityProxyInstance
        {
            DataStreamAccessWrapper<TInstance> dataStreamAccessWrapper = GetAccessWrapper<DataStreamAccessWrapper<TInstance>>(usage);
            return dataStreamAccessWrapper.DataStream;
        }

        internal CancelRequestsDataStream GetCancelRequestsDataStream(Usage usage)
        {
            CancelRequestsAccessWrapper dataStreamAccessWrapper = GetAccessWrapper<CancelRequestsAccessWrapper>(usage);
            return dataStreamAccessWrapper.CancelRequestsDataStream;
        }

        internal void GetCancelRequestsDataStreamWithContext(Usage usage, out CancelRequestsDataStream dataStream, out byte context)
        {
            CancelRequestsAccessWrapper dataStreamAccessWrapper = GetAccessWrapper<CancelRequestsAccessWrapper>(usage);
            dataStream = dataStreamAccessWrapper.CancelRequestsDataStream;
            context = dataStreamAccessWrapper.Context;
        }


        internal TData GetData<TData>(Usage usage)
            where TData : struct
        {
            GenericDataAccessWrapper<TData> genericDataAccessWrapper = GetAccessWrapper<GenericDataAccessWrapper<TData>>(usage);
            return genericDataAccessWrapper.Data;
        }

        internal NativeArray<Entity> GetEntityNativeArrayFromQuery(Usage usage)
        {
            EntityQueryAccessWrapper entityQueryAccessWrapper = GetAccessWrapper<EntityQueryAccessWrapper>(usage);
            return entityQueryAccessWrapper.NativeArray;
        }

        internal NativeArray<T> GetIComponentDataNativeArrayFromQuery<T>(Usage usage)
            where T : struct, IComponentData
        {
            EntityQueryComponentAccessWrapper<T> entityQueryAccessWrapper = GetAccessWrapper<EntityQueryComponentAccessWrapper<T>>(usage);
            return entityQueryAccessWrapper.NativeArray;
        }

        internal CDFEReader<T> GetCDFEReader<T>()
            where T : struct, IComponentData
        {
            CDFEAccessWrapper<T> cdfeAccessWrapper = GetAccessWrapper<CDFEAccessWrapper<T>>(Usage.Read);
            return cdfeAccessWrapper.CreateCDFEReader();
        }

        internal CDFEWriter<T> GetCDFEWriter<T>()
            where T : struct, IComponentData
        {
            CDFEAccessWrapper<T> cdfeAccessWrapper = GetAccessWrapper<CDFEAccessWrapper<T>>(Usage.Write);
            return cdfeAccessWrapper.CreateCDFEUpdater();
        }

        internal DBFEForRead<T> GetDBFEForRead<T>()
            where T : struct, IBufferElementData
        {
            DynamicBufferAccessWrapper<T> dynamicBufferAccessWrapper = GetAccessWrapper<DynamicBufferAccessWrapper<T>>(Usage.Read);
            return dynamicBufferAccessWrapper.CreateDynamicBufferReader();
        }

        internal DBFEForExclusiveWrite<T> GetDBFEForExclusiveWrite<T>()
            where T : struct, IBufferElementData
        {
            DynamicBufferAccessWrapper<T> dynamicBufferAccessWrapper = GetAccessWrapper<DynamicBufferAccessWrapper<T>>(Usage.ExclusiveWrite);
            return dynamicBufferAccessWrapper.CreateDynamicBufferExclusiveWriter();
        }

        //*************************************************************************************************************
        // SAFETY
        //*************************************************************************************************************

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureIsHardened()
        {
            if (m_IsHardened == false)
            {
                throw new InvalidOperationException($"{this} is not hardened yet!");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureNotHardened()
        {
            if (m_IsHardened == true)
            {
                throw new InvalidOperationException($"{this} is already hardened!");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureWrapperExists(JobConfigDataID id)
        {
            if (!m_AccessWrappers.ContainsKey(id))
            {
                throw new InvalidOperationException($"Job configured by {this} tried to access {id.AccessWrapperType.GetReadableName()} data for {id.Usage} but it wasn't found. Did you call the right Require function?");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureWrapperValidity(JobConfigDataID id)
        {
            //Straight duplicate check
            if (m_AccessWrappers.ContainsKey(id))
            {
                throw new InvalidOperationException($"{this} is trying to require {id.AccessWrapperType.GetReadableName()} data for {id.Usage} but it is already being used! Only require the data for the same usage once!");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureWrapperUsage(AbstractAccessWrapper wrapper)
        {
            if (wrapper.Debug_WrapperType != typeof(DataStreamAccessWrapper<>))
            {
                return;
            }

            //Access checks
            switch (wrapper.ID.Usage)
            {
                case Usage.Update:
                    //While updating, the same type could be cancelling.
                    Debug_EnsureWrapperUsageValid(wrapper.ID, Usage.WritePendingCancel);
                    break;
                case Usage.Write:
                    //Allowed to read while writing because we are writing to UnsafeTypedStream and reading from NativeArray
                    Debug_EnsureWrapperUsageValid(wrapper.ID, Usage.Read);
                    break;
                case Usage.Read:
                    //Allowed to write while reading because we are writing to UnsafeTypedStream and reading from NativeArray
                    Debug_EnsureWrapperUsageValid(wrapper.ID, Usage.Write);
                    break;
                case Usage.WritePendingCancel:
                    //We'll be updating when writing to cancel
                    Debug_EnsureWrapperUsageValid(wrapper.ID, Usage.Update);
                    break;
                case Usage.Cancelling:
                    //When we're cancelling, we can read or write to others because we're operating on a different stream
                    Debug_EnsureWrapperUsageValid(wrapper.ID, Usage.Read, Usage.Write);
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Trying to switch on {nameof(wrapper.ID.Usage)} but no code path satisfies for {wrapper.ID.Usage}!");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureWrapperUsageValid(JobConfigDataID id, params Usage[] allowedUsages)
        {
            foreach (Usage usage in USAGE_TYPES)
            {
                //Don't check against ourself or any of the allowed usages
                if (id.Usage == usage
                 || allowedUsages.Contains(usage))
                {
                    continue;
                }

                JobConfigDataID checkID = new JobConfigDataID(id.AccessWrapperType, usage);
                if (m_AccessWrappers.ContainsKey(checkID))
                {
                    throw new InvalidOperationException($"{this} is trying to require {id.AccessWrapperType.GetReadableName()} data for {id.Usage} but the same type is being used for {usage} which is not allowed!");
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureNoScheduleInfo()
        {
            if (m_ScheduleInfo != null)
            {
                throw new InvalidOperationException($"{this} is trying to schedule a job but it already has Schedule Info {m_ScheduleInfo} defined! Only schedule one piece of data!");
            }
        }


        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureScheduleInfoExists()
        {
            if (m_ScheduleInfo == null)
            {
                throw new InvalidOperationException($"{this} does not have a {nameof(AbstractScheduleInfo)} yet! Please schedule on some data first.");
            }
        }
    }
}