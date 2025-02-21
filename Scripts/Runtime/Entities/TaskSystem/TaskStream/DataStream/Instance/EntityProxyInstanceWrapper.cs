using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    internal readonly struct EntityProxyInstanceWrapper<TInstance> : IEquatable<EntityProxyInstanceWrapper<TInstance>>
        where TInstance : unmanaged, IEntityProxyInstance
    {
        public static bool operator ==(EntityProxyInstanceWrapper<TInstance> lhs, EntityProxyInstanceWrapper<TInstance> rhs)
        {
            //Note that we are not checking if the Payload is equal because the wrapper is only for origin and lookup
            //checks. 
            Debug_EnsurePayloadsAreTheSame(lhs, rhs);
            return lhs.InstanceID == rhs.InstanceID;
        }

        public static bool operator !=(EntityProxyInstanceWrapper<TInstance> lhs, EntityProxyInstanceWrapper<TInstance> rhs)
        {
            return !(lhs == rhs);
        }

        public readonly EntityProxyInstanceID InstanceID;
        public readonly TInstance Payload;

        public EntityProxyInstanceWrapper(Entity entity, byte context, ref TInstance payload)
        {
            InstanceID = new EntityProxyInstanceID(entity, context);
            Payload = payload;
        }

        public bool Equals(EntityProxyInstanceWrapper<TInstance> other)
        {
            return this == other;
        }

        public override bool Equals(object compare)
        {
            return compare is EntityProxyInstanceWrapper<TInstance> id && Equals(id);
        }

        public override int GetHashCode()
        {
            //We ignore the Payload because the wrapper is only for origin and lookup checks.
            return InstanceID.GetHashCode();
        }

        public override string ToString()
        {
            return $"{InstanceID.ToString()})";
        }

        [BurstCompatible]
        public FixedString64Bytes ToFixedString()
        {
            return InstanceID.ToFixedString();
        }


        //*************************************************************************************************************
        // SAFETY
        //*************************************************************************************************************

        [Conditional("ANVIL_DEBUG_SAFETY_EXPENSIVE")]
        private static void Debug_EnsurePayloadsAreTheSame(EntityProxyInstanceWrapper<TInstance> lhs, EntityProxyInstanceWrapper<TInstance> rhs)
        {
            if (lhs.InstanceID == rhs.InstanceID
             && !lhs.Payload.Equals(rhs.Payload))
            {
                throw new InvalidOperationException($"Equality check for {typeof(EntityProxyInstanceWrapper<TInstance>)} where the ID's are the same but the Payloads are different. This should never happen!");
            }
        }
    }
}
