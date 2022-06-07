using Anvil.Unity.DOTS.Jobs;
using Unity.Collections;
using Unity.Jobs;

namespace Anvil.Unity.DOTS.Data
{
    public class VData<T> : AbstractVData
        where T : struct
    {
        private UnsafeTypedStream<T> m_Pending;
        private DeferredNativeArray<T> m_Current;

        public VData() : this(NULL_VDATA)
        {
        }

        public VData(AbstractVData input) : base(input)
        {
            m_Pending = new UnsafeTypedStream<T>(Allocator.Persistent,
                                                 Allocator.TempJob);
            m_Current = new DeferredNativeArray<T>(Allocator.Persistent,
                                                   Allocator.TempJob);
        }

        protected override void DisposeSelf()
        {
            m_Pending.Dispose();
            m_Current.Dispose();

            base.DisposeSelf();
        }

        public DeferredNativeArray<T> ArrayForScheduling
        {
            get => m_Current;
        }

        public JobDataForCompletion<T> GetCompletionWriter()
        {
            return new JobDataForCompletion<T>(m_Pending.AsWriter());
        }

        
        public JobHandle AcquireForAdd(out JobDataForAdd<T> workStruct)
        {
            //TODO: Collections Checks
            JobHandle sharedWriteHandle = AccessController.AcquireAsync(AccessType.SharedWrite);

            workStruct = new JobDataForAdd<T>(m_Pending.AsWriter());

            return sharedWriteHandle;
        }

        public void ReleaseForAdd(JobHandle releaseAccessDependency)
        {
            //TODO: Collections Checks
            AccessController.ReleaseAsync(releaseAccessDependency);
        }

        public JobHandle AcquireForWork(JobHandle dependsOn, out JobDataForWork<T> workStruct)
        {
            //TODO: Collections Checks
            JobHandle exclusiveWriteHandle = AccessController.AcquireAsync(AccessType.ExclusiveWrite);

            //Consolidate everything in pending into current so it can be balanced across threads
            ConsolidateToNativeArrayJob<T> consolidateJob = new ConsolidateToNativeArrayJob<T>(m_Pending.AsReader(),
                                                                                               m_Current);
            JobHandle consolidateHandle = consolidateJob.Schedule(JobHandle.CombineDependencies(dependsOn, exclusiveWriteHandle));

            //Clear pending so we can use it again
            JobHandle clearHandle = m_Pending.Clear(consolidateHandle);

            //Create the work struct
            workStruct = new JobDataForWork<T>(m_Pending.AsWriter(),
                                               m_Current.AsDeferredJobArray());

            

            return AcquireOutputsAsync(clearHandle);
        }

        public void ReleaseForWork(JobHandle releaseAccessDependency)
        {
            //TODO: Collections Checks
            AccessController.ReleaseAsync(releaseAccessDependency);
            ReleaseOutputsAsync(releaseAccessDependency);
        }
    }

    public struct JobDataForAdd<T>
        where T : struct
    {
        private const int DEFAULT_LANE_INDEX = -1;

        [ReadOnly] private readonly UnsafeTypedStream<T>.Writer m_AddWriter;

        private UnsafeTypedStream<T>.LaneWriter m_AddLaneWriter;

        public int LaneIndex
        {
            get;
            private set;
        }

        public JobDataForAdd(UnsafeTypedStream<T>.Writer addWriter) : this()
        {
            m_AddWriter = addWriter;

            m_AddLaneWriter = default;
            LaneIndex = DEFAULT_LANE_INDEX;
        }

        public void InitForThread(int nativeThreadIndex)
        {
            //TODO: Collections Checks
            LaneIndex = ParallelAccessUtil.CollectionIndexForThread(nativeThreadIndex);
            m_AddLaneWriter = m_AddWriter.AsLaneWriter(LaneIndex);
        }

        public void Add(T value)
        {
            //TODO: Collections Checks
            m_AddLaneWriter.Write(ref value);
        }

        public void Add(ref T value)
        {
            //TODO: Collections Checks
            m_AddLaneWriter.Write(ref value);
        }
    }

    public readonly struct JobDataForCompletion<T>
        where T : struct
    {
        [ReadOnly] private readonly UnsafeTypedStream<T>.Writer m_CompletionWriter;

        public JobDataForCompletion(UnsafeTypedStream<T>.Writer completionWriter)
        {
            m_CompletionWriter = completionWriter;
        }

        public void Add(T value, int laneIndex)
        {
            Add(ref value, laneIndex);
        }

        public void Add(ref T value, int laneIndex)
        {
            m_CompletionWriter.AsLaneWriter(laneIndex).Write(ref value);
        }
    }


    public struct JobDataForWork<T>
        where T : struct
    {
        private const int DEFAULT_LANE_INDEX = -1;

        [ReadOnly] private readonly UnsafeTypedStream<T>.Writer m_ContinueWriter;
        [ReadOnly] private readonly NativeArray<T> m_Current;

        private UnsafeTypedStream<T>.LaneWriter m_ContinueLaneWriter;

        public int LaneIndex
        {
            get;
            private set;
        }

        public JobDataForWork(UnsafeTypedStream<T>.Writer continueWriter,
                              NativeArray<T> current)
        {
            m_ContinueWriter = continueWriter;
            m_Current = current;

            m_ContinueLaneWriter = default;
            LaneIndex = DEFAULT_LANE_INDEX;
        }

        public void InitForThread(int nativeThreadIndex)
        {
            //TODO: Collections Checks
            LaneIndex = ParallelAccessUtil.CollectionIndexForThread(nativeThreadIndex);
            m_ContinueLaneWriter = m_ContinueWriter.AsLaneWriter(LaneIndex);
        }

        public T this[int index]
        {
            //TODO: Collections Checks
            get => m_Current[index];
        }

        internal void Continue(ref T value)
        {
            //TODO: Collections Checks
            m_ContinueLaneWriter.Write(ref value);
        }
    }
}
