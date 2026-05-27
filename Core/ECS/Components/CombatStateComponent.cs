namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Transient combat metadata. Holds the entity id of this entity's current opponent.
    /// Companion to <c>EntityState.InCombat</c>: the state flag records that combat is active;
    /// this component records who this entity is fighting.
    /// Not <c>[Persistent]</c> — stale combat state on restart would reference entities that
    /// may not exist. Cleared on crash by design.
    /// </summary>
    public sealed class CombatStateComponent : IComponent
    {
        public uint OpponentEntityId { get; set; }
    }
}
