using Anvil.CSharp.Core;
using Anvil.Unity.DOTS.Data;
using Anvil.Unity.DOTS.Jobs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    //Helper class to manage writing cancellation requests (which come in from one TaskDriver) to that TaskDriver's 
    //TaskSystem and to all sub-TaskDrivers in one quick job.
    internal class TaskDriverCancellationPropagator : AbstractAnvilBase
    {
        internal static readonly BulkScheduleDelegate<TaskDriverCancellationPropagator> CONSOLIDATE_AND_PROPAGATE_SCHEDULE_FUNCTION = BulkSchedulingUtil.CreateSchedulingDelegate<TaskDriverCancellationPropagator>(nameof(ConsolidateAndPropagate), BindingFlags.Instance | BindingFlags.NonPublic);
    
        private readonly CancelRequestsDataStream m_TaskDriverCancelRequests;

        private readonly CancelRequestsDataStream m_SystemCancelRequests;
        private readonly UnsafeTypedStream<EntityProxyInstanceID>.Writer m_SystemPendingWriter;
        
        private readonly List<CancelRequestsDataStream> m_SubTaskDriverCancelRequests;
        private NativeArray<UnsafeTypedStream<EntityProxyInstanceID>.Writer> m_SubTaskDriverPendingWriters;
        private NativeArray<UnsafeTypedStream<EntityProxyInstanceID>.LaneWriter> m_SubTaskDriverPendingLaneWriters;

        private readonly int m_TaskDriverDependencyIndex;
        private readonly int m_SystemDependencyIndex;
        private readonly int m_IncomingDependencyIndex;
        
        
        private NativeArray<JobHandle> m_Dependencies;

        public AbstractTaskDriver TaskDriver { get; }

        public TaskDriverCancellationPropagator(AbstractTaskDriver taskDriver, 
                                                CancelRequestsDataStream taskDriverCancelRequests, 
                                                CancelRequestsDataStream systemCancelRequests, 
                                                List<CancelRequestsDataStream> subTaskDriverCancelRequests)
        {
            TaskDriver = taskDriver;
            m_TaskDriverCancelRequests = taskDriverCancelRequests;

            m_SystemCancelRequests = systemCancelRequests;
            m_SystemPendingWriter = m_SystemCancelRequests.PendingRef.AsWriter();
            
            m_SubTaskDriverCancelRequests = subTaskDriverCancelRequests;
            m_SubTaskDriverPendingWriters = new NativeArray<UnsafeTypedStream<EntityProxyInstanceID>.Writer>(m_SubTaskDriverCancelRequests.Count, Allocator.Persistent);
            m_SubTaskDriverPendingLaneWriters = new NativeArray<UnsafeTypedStream<EntityProxyInstanceID>.LaneWriter>(m_SubTaskDriverPendingWriters.Length, Allocator.Persistent);
            for (int i = 0; i < m_SubTaskDriverPendingWriters.Length; ++i)
            {
                m_SubTaskDriverPendingWriters[i] = m_SubTaskDriverCancelRequests[i].PendingRef.AsWriter();
            }

            int numSubDrivers = m_SubTaskDriverCancelRequests.Count;
            
            m_Dependencies = new NativeArray<JobHandle>(numSubDrivers + 3, Allocator.Persistent);
            m_SystemDependencyIndex = numSubDrivers;
            m_TaskDriverDependencyIndex = numSubDrivers + 1;
            m_IncomingDependencyIndex = numSubDrivers + 2;
        }

        protected override void DisposeSelf()
        {
            if (m_Dependencies.IsCreated)
            {
                m_Dependencies.Dispose();
            }

            if (m_SubTaskDriverPendingWriters.IsCreated)
            {
                m_SubTaskDriverPendingWriters.Dispose();
            }

            if (m_SubTaskDriverPendingLaneWriters.IsCreated)
            {
                m_SubTaskDriverPendingLaneWriters.Dispose();
            }
            base.DisposeSelf();
        }

        private JobHandle ConsolidateAndPropagate(JobHandle dependsOn)
        {
            for (int i = 0; i < m_SubTaskDriverCancelRequests.Count; ++i)
            {
                m_Dependencies[i] = m_SubTaskDriverCancelRequests[i].AccessController.AcquireAsync(AccessType.SharedWrite);
            }

            m_Dependencies[m_SystemDependencyIndex] = m_SystemCancelRequests.AccessController.AcquireAsync(AccessType.SharedWrite);
            m_Dependencies[m_TaskDriverDependencyIndex] = m_TaskDriverCancelRequests.AccessController.AcquireAsync(AccessType.SharedRead);
            m_Dependencies[m_IncomingDependencyIndex] = dependsOn;
            
            ConsolidateAndPropagateCancelRequestsJob job = new ConsolidateAndPropagateCancelRequestsJob(ref m_TaskDriverCancelRequests.PendingRef,
                                                                                                        ref m_TaskDriverCancelRequests.LookupRef,
                                                                                                        m_SystemPendingWriter,
                                                                                                        m_SubTaskDriverPendingWriters,
                                                                                                        m_SubTaskDriverPendingLaneWriters);
            dependsOn = job.Schedule(JobHandle.CombineDependencies(m_Dependencies));

            foreach (CancelRequestsDataStream cancelStream in m_SubTaskDriverCancelRequests)
            {
                cancelStream.AccessController.ReleaseAsync(dependsOn);
            }
            m_SystemCancelRequests.AccessController.ReleaseAsync(dependsOn);
            m_TaskDriverCancelRequests.AccessController.ReleaseAsync(dependsOn);
            
            return dependsOn;
        }
        
        //*************************************************************************************************************
        // JOBS
        //*************************************************************************************************************
        
        [BurstCompile]
        private struct ConsolidateAndPropagateCancelRequestsJob : IJob
        {
            private const int UNSET_NATIVE_THREAD_INDEX = -1;
            
            [ReadOnly] private UnsafeTypedStream<EntityProxyInstanceID> m_Pending;
            private UnsafeParallelHashMap<EntityProxyInstanceID, byte> m_Lookup;

            private readonly UnsafeTypedStream<EntityProxyInstanceID>.Writer m_PendingSystemWriter;
            private NativeArray<UnsafeTypedStream<EntityProxyInstanceID>.Writer> m_PendingSubTaskDriverWriters;
            private NativeArray<UnsafeTypedStream<EntityProxyInstanceID>.LaneWriter> m_PendingSubTaskDriverLaneWriters;

            [NativeSetThreadIndex] private readonly int m_NativeThreadIndex;

            public ConsolidateAndPropagateCancelRequestsJob(ref UnsafeTypedStream<EntityProxyInstanceID> pending,
                                                            ref UnsafeParallelHashMap<EntityProxyInstanceID, byte> lookup,
                                                            UnsafeTypedStream<EntityProxyInstanceID>.Writer pendingSystemWriter,
                                                            NativeArray<UnsafeTypedStream<EntityProxyInstanceID>.Writer> pendingSubTaskDriverWriters,
                                                            NativeArray<UnsafeTypedStream<EntityProxyInstanceID>.LaneWriter> pendingSubTaskDriverLaneWriters)
            {
                m_Pending = pending;
                m_Lookup = lookup;
                m_PendingSystemWriter = pendingSystemWriter;
                m_PendingSubTaskDriverWriters = pendingSubTaskDriverWriters;
                m_PendingSubTaskDriverLaneWriters = pendingSubTaskDriverLaneWriters;
                
                m_NativeThreadIndex = UNSET_NATIVE_THREAD_INDEX;
            }

            public void Execute()
            {
                int laneIndex = ParallelAccessUtil.CollectionIndexForThread(m_NativeThreadIndex);
                
                UnsafeTypedStream<EntityProxyInstanceID>.LaneWriter pendingSystemLaneWriter = m_PendingSystemWriter.AsLaneWriter(laneIndex);
                for (int i = 0; i < m_PendingSubTaskDriverWriters.Length; ++i)
                {
                    m_PendingSubTaskDriverLaneWriters[i] = m_PendingSubTaskDriverWriters[i].AsLaneWriter(laneIndex);
                }

                m_Lookup.Clear();

                foreach (EntityProxyInstanceID proxyInstanceID in m_Pending)
                {
                    Debug_EnsureNoDuplicates(proxyInstanceID);
                    m_Lookup.TryAdd(proxyInstanceID, 1);
                    
                    pendingSystemLaneWriter.Write(proxyInstanceID);
                    for (int i = 0; i < m_PendingSubTaskDriverLaneWriters.Length; ++i)
                    {
                        m_PendingSubTaskDriverLaneWriters[i].Write(proxyInstanceID);
                    }
                }

                m_Pending.Clear();
            }
            
            //*************************************************************************************************************
            // SAFETY
            //*************************************************************************************************************

            [Conditional("ANVIL_DEBUG_SAFETY_EXPENSIVE")]
            private void Debug_EnsureNoDuplicates(EntityProxyInstanceID id)
            {
                if (m_Lookup.ContainsKey(id))
                {
                    throw new InvalidOperationException($"Trying to add id of {id} but the same id already exists in the lookup! This should never happen! Investigate.");
                }
            }
        }
    }
}
