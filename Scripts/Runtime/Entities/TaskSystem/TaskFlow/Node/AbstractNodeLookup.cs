namespace Anvil.Unity.DOTS.Entities.Tasks
{
    internal abstract class AbstractNodeLookup

    {
        public TaskFlowGraph TaskGraph
        {
            get;
        }

        public AbstractTaskSystem TaskSystem
        {
            get;
        }

        public AbstractTaskDriver TaskDriver
        {
            get;
        }

        protected AbstractNodeLookup(TaskFlowGraph taskGraph, AbstractTaskSystem taskSystem, AbstractTaskDriver taskDriver)
        {
            TaskGraph = taskGraph;
            TaskSystem = taskSystem;
            TaskDriver = taskDriver;
        }
    }
}
