using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Attributes.Systems
{
    /// <summary>
    /// Read/write seam for <see cref="AttributesComponent"/> and <see cref="PoolsComponent"/>, and the
    /// single owner of a pool's <b>effective cap</b>. Getters are the surface combat and stat
    /// consumers call; setters serve the admin and initialization paths.
    /// INV-5: this system never touches the event bus or persistence.
    /// <para>
    /// Pool invariants (all four pools, keyed by <see cref="ResourceType"/>):
    /// <list type="bullet">
    /// <item>The effective cap is the stored base max plus every active <c>&lt;Pool&gt;Max</c> modifier
    /// (effects and contributors via <c>IEffectSystem.GetModifiers</c>), floored at 1 when the base is
    /// above 0 (0 otherwise). It is computed on read and never stored (INV-24).</item>
    /// <item>Reads of current are clamped: <c>min(stored, cap)</c>. A dropped cap is visible at once;
    /// the heartbeat reconcile writes the clamp back to storage.</item>
    /// <item>Writes clamp to <c>[floor, cap]</c>, where the floor is <c>Death:HpFloor</c> (default -10) for
    /// HP and 0 for the other pools.</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface IAttributeSystem
    {
        int GetLevel(uint entityId);
        int GetMind(uint entityId);
        int GetBody(uint entityId);
        int GetSpirit(uint entityId);
        int GetAttunement(uint entityId);

        /// <summary>Stored base max HP. Read <see cref="GetEffectiveMax"/> for the cap.</summary>
        int GetBaseMaxHp(uint entityId);
        /// <summary>Clamped current HP (<see cref="GetCurrent"/>).</summary>
        int GetCurrentHp(uint entityId);
        /// <summary>Stored base max mana. Read <see cref="GetEffectiveMax"/> for the cap.</summary>
        int GetBaseMaxMana(uint entityId);
        int GetCurrentMana(uint entityId);
        /// <summary>Stored base max stamina. Read <see cref="GetEffectiveMax"/> for the cap.</summary>
        int GetBaseMaxStamina(uint entityId);
        int GetCurrentStamina(uint entityId);
        /// <summary>Stored base max astra. Read <see cref="GetEffectiveMax"/> for the cap.</summary>
        int GetBaseMaxAstra(uint entityId);
        int GetCurrentAstra(uint entityId);

        /// <summary>Base max + active <c>&lt;Pool&gt;Max</c> modifiers, floored at 1 when base &gt; 0 (0 otherwise).</summary>
        int GetEffectiveMax(uint entityId, ResourceType pool);

        /// <summary>
        /// Clamped current: <c>min(stored, GetEffectiveMax)</c>. A negative stored HP (incapacitated,
        /// bleeding out) reads unchanged.
        /// </summary>
        int GetCurrent(uint entityId, ResourceType pool);

        /// <summary>
        /// Stores <c>clamp(value, floor, GetEffectiveMax)</c>. No events, no persistence (INV-5). For HP
        /// the caller evaluates threshold crossings via <c>IDeathSystem.OnHpChanged</c> — this method
        /// does NOT call it.
        /// </summary>
        void SetCurrent(uint entityId, ResourceType pool, int value);

        /// <summary>Stores a new <b>base</b> max, then clamps stored current to the new effective cap.</summary>
        void SetMax(uint entityId, ResourceType pool, int value);

        /// <summary>Stores <c>floor(GetEffectiveMax × fraction)</c> as current (1.0 = fill; respawn uses its percent).</summary>
        void RestoreToFraction(uint entityId, ResourceType pool, double fraction);

        /// <summary><see cref="RestoreToFraction"/> for all four pools (spawn/creation fill, respawn).</summary>
        void RestoreAllToFraction(uint entityId, double fraction);

        /// <summary>
        /// Writes <c>min(stored, cap)</c> back to storage for all four pools. Never raises a value.
        /// Returns whether any pool changed.
        /// </summary>
        bool ReconcilePools(uint entityId);

        /// <summary>Reconciles every <see cref="PoolsComponent"/> entity; returns the number of pool values corrected.</summary>
        int ReconcileAllPools();

        void SetLevel(uint entityId, int value);
        void SetMind(uint entityId, int value);
        void SetBody(uint entityId, int value);
        void SetSpirit(uint entityId, int value);
        void SetAttunement(uint entityId, int value);

        /// <summary>
        /// Sets base MaxHp (<see cref="SetMax"/>). Note: a non-positive base floors the cap at 0, which
        /// can write positive HP to 0 without <c>IDeathSystem.OnHpChanged</c> — admin paths only.
        /// </summary>
        void SetMaxHp(uint entityId, int value);
        /// <summary>Sets CurrentHp (<see cref="SetCurrent"/>); clamped to [<c>Death:HpFloor</c>, effective cap].</summary>
        void SetCurrentHp(uint entityId, int value);

        void SetMaxMana(uint entityId, int value);
        void SetCurrentMana(uint entityId, int value);
        void SetMaxStamina(uint entityId, int value);
        void SetCurrentStamina(uint entityId, int value);
        void SetMaxAstra(uint entityId, int value);
        void SetCurrentAstra(uint entityId, int value);
    }
}
