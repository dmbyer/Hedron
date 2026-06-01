namespace Hedron.Core.Modules.Death.Systems
{
    /// <summary>
    /// Result returned by <see cref="IDeathSystem.OnHpChanged"/> describing which threshold,
    /// if any, was crossed.
    /// </summary>
    public enum DeathTransition
    {
        /// <summary>No threshold was crossed. HP changed but the entity is neither
        /// newly incapacitated nor dead.</summary>
        None,

        /// <summary>HP crossed from above zero to zero-or-below for a non-incapacitated
        /// entity. The entity is now incapacitated.</summary>
        BecameIncapacitated,

        /// <summary>HP reached or dropped below <c>Death:HpFloor</c>. The entity has died.</summary>
        Died,
    }

    /// <summary>
    /// Domain system for the player death and respawn lifecycle.
    /// Pure: never touches the event bus or persistence (INV-5, INV-8).
    /// Callers (handlers, initiators) are responsible for reading the returned
    /// <see cref="DeathTransition"/> and publishing the appropriate events.
    /// </summary>
    public interface IDeathSystem
    {
        /// <summary>
        /// Evaluates HP-threshold crossings after an HP mutation.
        /// Must be called by the handler that mutated HP — NOT by <see cref="Hedron.Core.Modules.Attributes.Systems.IAttributeSystem"/>
        /// (INV-5: a core system must not chain into a domain decision).
        /// </summary>
        /// <param name="entityId">The entity whose HP changed.</param>
        /// <param name="previousHp">The HP value before the mutation.</param>
        /// <param name="newHp">The HP value after the mutation.</param>
        /// <returns>
        /// <see cref="DeathTransition.BecameIncapacitated"/> when <paramref name="newHp"/> &lt;= 0
        /// and <paramref name="previousHp"/> &gt; 0 and the entity is not already incapacitated.
        /// <see cref="DeathTransition.Died"/> when <paramref name="newHp"/> &lt;= <c>Death:HpFloor</c>.
        /// <see cref="DeathTransition.None"/> otherwise.
        /// </returns>
        DeathTransition OnHpChanged(uint entityId, int previousHp, int newHp);

        /// <summary>
        /// Performs the full respawn mutation on <paramref name="entityId"/>:
        /// exits the <c>Incapacitated</c> state, relocates to the stored respawn room,
        /// strips impermanent effects, and restores all four pools to a configured
        /// percentage of their maxima.
        /// </summary>
        void Respawn(uint entityId);

        /// <summary>
        /// Validates that <paramref name="roomBlueprintId"/> exists in the template registry,
        /// then sets <c>RespawnComponent.RoomBlueprintId</c> on <paramref name="entityId"/>.
        /// </summary>
        /// <returns><c>true</c> on success; <c>false</c> when the blueprint is not found,
        /// in which case <paramref name="failReason"/> is set to a human-readable message.</returns>
        bool SetRespawn(uint entityId, string roomBlueprintId, out string? failReason);
    }
}
