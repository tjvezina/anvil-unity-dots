using Anvil.CSharp.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    internal class JobRouteNode : AbstractNode
    {
        private readonly Dictionary<AbstractJobConfig, JobNode> m_JobsByConfig;
        private readonly JobNodeLookup m_Lookup;

        public TaskFlowRoute Route
        {
            get;
        }

        public JobRouteNode(JobNodeLookup lookup,
                            TaskFlowRoute route,
                            TaskFlowGraph taskFlowGraph,
                            AbstractTaskSystem taskSystem,
                            AbstractTaskDriver taskDriver) : base(taskFlowGraph, taskSystem, taskDriver)
        {
            m_Lookup = lookup;
            Route = route;
            m_JobsByConfig = new Dictionary<AbstractJobConfig, JobNode>();
        }

        public void CreateNode(AbstractJobConfig jobConfig)
        {
            Debug_EnsureNoDuplicateNodes(jobConfig);
            JobNode node = new JobNode(this,
                                       Route,
                                       jobConfig,
                                       TaskFlowGraph,
                                       TaskSystem,
                                       TaskDriver);
            m_JobsByConfig.Add(jobConfig, node);
        }

        public void AddJobConfigsTo(List<AbstractJobConfig> jobConfigs)
        {
            jobConfigs.AddRange(m_JobsByConfig.Keys);
        }


        //*************************************************************************************************************
        // SAFETY
        //*************************************************************************************************************

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureNoDuplicateNodes(AbstractJobConfig config)
        {
            if (m_JobsByConfig.ContainsKey(config))
            {
                throw new InvalidOperationException($"Trying to create a new {nameof(JobNode)} with config of {config} but one already exists!");
            }
        }
    }
}
