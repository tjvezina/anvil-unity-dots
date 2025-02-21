using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    /// <summary>
    /// Job Scheduling Delegates that are called when scheduling a Job via Unity's job scheduling code.
    /// </summary>
    public static class JobConfigScheduleDelegates
    {
        /// <summary>
        /// For scheduling a job triggered by a <see cref="TaskStream{TInstance}"/> from
        /// a <see cref="TaskStreamJobConfig{TInstance}"/>
        /// </summary>
        /// <typeparam name="TInstance">The type of <see cref="IEntityProxyInstance"/></typeparam>
        public delegate JobHandle ScheduleTaskStreamJobDelegate<TInstance>(JobHandle jobHandle,
                                                                           TaskStreamJobData<TInstance> jobData,
                                                                           TaskStreamScheduleInfo<TInstance> scheduleInfo)
            where TInstance : unmanaged, IEntityProxyInstance;

        /// <summary>
        /// For scheduling a job triggered by the entities in an <see cref="EntityQuery"/> from
        /// an <see cref="EntityQueryJobConfig"/>
        /// </summary>
        public delegate JobHandle ScheduleEntityQueryJobDelegate(JobHandle jobHandle,
                                                                 EntityQueryJobData jobData,
                                                                 EntityQueryScheduleInfo scheduleInfo);
        
        /// <summary>
        /// For scheduling a job triggered by the <see cref="IComponentData"/> in an <see cref="EntityQuery"/> from
        /// an <see cref="EntityQueryComponentJobConfig{T}"/>
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IComponentData"/></typeparam>
        public delegate JobHandle ScheduleEntityQueryComponentJobDelegate<T>(JobHandle jobHandle,
                                                                             EntityQueryComponentJobData<T> jobData,
                                                                             EntityQueryComponentScheduleInfo<T> scheduleInfo)
            where T : struct, IComponentData;

        /// <summary>
        /// For scheduling a job triggered by instances in a <see cref="NativeArray{T}"/> from a
        /// <see cref="NativeArrayJobConfig{T}"/>
        /// </summary>
        /// <typeparam name="T">The type of struct in the array</typeparam>
        public delegate JobHandle ScheduleNativeArrayJobDelegate<T>(JobHandle jobHandle,
                                                                    NativeArrayJobData<T> jobData,
                                                                    NativeArrayScheduleInfo<T> scheduleInfo)
            where T : struct;


        /// <summary>
        /// For scheduling a job triggered by instances in a <see cref="TaskStream{TInstance}"/> that need to be
        /// updated. Used with <see cref="UpdateJobConfig{TInstance}"/>
        /// </summary>
        /// <typeparam name="TInstance">The type of <see cref="IEntityProxyInstance"/></typeparam>
        public delegate JobHandle ScheduleUpdateJobDelegate<TInstance>(JobHandle jobHandle,
                                                                       UpdateJobData<TInstance> jobData,
                                                                       UpdateTaskStreamScheduleInfo<TInstance> scheduleInfo)
            where TInstance : unmanaged, IEntityProxyInstance;

        /// <summary>
        /// For scheduling a job triggered by instances in a <see cref="TaskStream{TInstance}"/> that have been
        /// requested to cancel. Used with <see cref="CancelJobConfig{TInstance}"/>
        /// </summary>
        /// <typeparam name="TInstance">The type of <see cref="IEntityProxyInstance"/></typeparam>
        public delegate JobHandle ScheduleCancelJobDelegate<TInstance>(JobHandle jobHandle,
                                                                       CancelJobData<TInstance> jobData,
                                                                       CancelTaskStreamScheduleInfo<TInstance> scheduleInfo)
            where TInstance : unmanaged, IEntityProxyInstance;
    }
}
