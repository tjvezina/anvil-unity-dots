using Unity.Collections;
using Unity.Entities;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    /// <summary>
    /// Represents a <see cref="BufferFromEntity{T}"/> that can only be read from.
    /// Each <see cref="DynamicBuffer{T}"/> can be read in a separate thread in parallel.
    /// To be used in jobs that allow for reading a specific instance in the DBFE
    /// </summary>
    /// <typeparam name="T">The type of <see cref="IBufferElementData"/> to update.</typeparam>
    [BurstCompatible]
    public readonly struct DBFEForRead<T>
        where T : struct, IBufferElementData
    {
        [ReadOnly] private readonly BufferFromEntity<T> m_DBFE;

        internal DBFEForRead(SystemBase system)
        {
            m_DBFE = system.GetBufferFromEntity<T>(true);
        }
        
        /// <summary>
        /// Gets the <see cref="DynamicBuffer{T}"/> that corresponds to the passed <see cref="Entity"/>
        /// </summary>
        /// <param name="entity">The <see cref="Entity"/> to lookup the <see cref="DynamicBuffer{T}"/></param>
        public DynamicBuffer<T> this[Entity entity]
        {
            get => m_DBFE[entity];
        }
    }
}
