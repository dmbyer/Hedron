using System;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Two-axis protection flags for an entity. Carried by mobs (and any entity)
    /// that should be invulnerable to attack, immune to effects, or both.
    /// World-content component — NOT <c>[Persistent]</c> (durable form is the mob YAML template).
    /// </summary>
    [Flags]
    public enum ProtectionFlags
    {
        None          = 0,
        /// <summary>Entity cannot be the target of a melee or ability attack.</summary>
        Untargetable  = 1 << 0,
        /// <summary>Entity rejects all effects — beneficial and harmful alike.</summary>
        EffectImmune  = 1 << 1,
    }

    /// <summary>
    /// Carries <see cref="ProtectionFlags"/> on an entity. No logic lives here (INV-3).
    /// </summary>
    public sealed class ProtectionComponent : IComponent
    {
        public ProtectionFlags Flags { get; set; } = ProtectionFlags.None;
    }
}
