using Anvil.Unity.DOTS.Jobs;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    internal class DataStreamAsResolveTargetAccessWrapper : AbstractAccessWrapper
    {
        public static DataStreamAsResolveTargetAccessWrapper Create<TResolveTarget>(TResolveTarget resolveTarget, List<ResolveTargetData> resolveTargetData)
            where TResolveTarget : Enum
        {
            ResolveTargetUtil.Debug_EnsureEnumValidity(resolveTarget);
            return new DataStreamAsResolveTargetAccessWrapper(resolveTargetData);
        }

        private readonly List<ResolveTargetData> m_ResolveTargetData;
        private NativeArray<JobHandle> m_Dependencies;

        private DataStreamAsResolveTargetAccessWrapper(List<ResolveTargetData> resolveTargetData) : base(AccessType.SharedWrite)
        {
            m_ResolveTargetData = resolveTargetData;
            m_Dependencies = new NativeArray<JobHandle>(m_ResolveTargetData.Count, Allocator.Persistent);
        }

        protected override void DisposeSelf()
        {
            m_Dependencies.Dispose();
            base.DisposeSelf();
        }

        public sealed override JobHandle Acquire()
        {
            for (int i = 0; i < m_ResolveTargetData.Count; ++i)
            {
                m_Dependencies[i] = m_ResolveTargetData[i].DataStream.AccessController.AcquireAsync(AccessType);
            }

            return JobHandle.CombineDependencies(m_Dependencies);
        }

        public sealed override void Release(JobHandle releaseAccessDependency)
        {
            foreach (ResolveTargetData resolveTargetData in m_ResolveTargetData)
            {
                resolveTargetData.DataStream.AccessController.ReleaseAsync(releaseAccessDependency);
            }
        }
    }
}
