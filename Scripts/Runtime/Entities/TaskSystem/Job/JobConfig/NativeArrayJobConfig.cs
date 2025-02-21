using Anvil.Unity.DOTS.Jobs;
using Unity.Collections;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    internal class NativeArrayJobConfig<T> : AbstractJobConfig
        where T : struct
    {
        public NativeArrayJobConfig(TaskFlowGraph taskFlowGraph,
                                    AbstractTaskSystem taskSystem,
                                    AbstractTaskDriver taskDriver,
                                    AccessControlledValue<NativeArray<T>> nativeArray)
            : base(taskFlowGraph,
                   taskSystem,
                   taskDriver)
        {
            RequireDataForRead(nativeArray);
        }
    }
}
