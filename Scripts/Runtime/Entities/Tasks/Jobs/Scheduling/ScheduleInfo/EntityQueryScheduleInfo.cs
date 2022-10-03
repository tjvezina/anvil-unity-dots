using Unity.Entities;
using Unity.Jobs;

namespace Anvil.Unity.DOTS.Entities
{
    internal class EntityQueryScheduleInfo : AbstractScheduleInfo,
                                             IScheduleInfo
    {
        private readonly JobConfigScheduleDelegates.ScheduleJobDelegate m_ScheduleJobFunction;
        private readonly EntityQueryJobData m_JobData;

        public int BatchSize { get; }

        public EntityQueryNativeArray EntityQueryNativeArray { get; }

        public int Length
        {
            get => EntityQueryNativeArray.Length;
        }

        public EntityQueryScheduleInfo(EntityQueryJobData jobData,
                                       EntityQueryNativeArray entityQueryNativeArray,
                                       BatchStrategy batchStrategy,
                                       JobConfigScheduleDelegates.ScheduleJobDelegate scheduleJobFunction) : base(scheduleJobFunction.Method)
        {
            m_JobData = jobData;
            EntityQueryNativeArray = entityQueryNativeArray;

            BatchSize = batchStrategy == BatchStrategy.MaximizeChunk
                ? ChunkUtil.MaxElementsPerChunk<Entity>()
                : 1;

            m_ScheduleJobFunction = scheduleJobFunction;
        }

        public sealed override JobHandle CallScheduleFunction(JobHandle dependsOn)
        {
            return m_ScheduleJobFunction(dependsOn, m_JobData, this);
        }
    }
}
