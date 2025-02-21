using Unity.Entities;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    /// <summary>
    /// Triggering specific <see cref="AbstractJobData"/> for use when triggering the job based on an <see cref="EntityQuery"/>
    /// that requires <see cref="IComponentData"/>.
    /// </summary>
    /// <typeparam name="T">The type of <see cref="IComponentData"/></typeparam>
    public class EntityQueryComponentJobData<T> : AbstractJobData
        where T : struct, IComponentData
    {
        private readonly EntityQueryComponentJobConfig<T> m_JobConfig;
        
        internal EntityQueryComponentJobData(EntityQueryComponentJobConfig<T> jobConfig, World world, byte context) : base(world, context, jobConfig)
        {
            m_JobConfig = jobConfig;
        }
    }
}
