namespace Hedron.Core.Modules.Attributes.Systems
{
    /// <summary>
    /// Read/write seam for <see cref="ECS.Components.AttributesComponent"/> and
    /// <see cref="ECS.Components.PoolsComponent"/>. Getters are the surface combat and stat
    /// consumers call; setters serve the admin and initialization paths.
    /// INV-5: this system never touches the event bus or persistence.
    /// Pool invariants: SetMaxX clamps CurrentX to new MaxX; SetCurrentX clamps to [0, MaxX].
    /// </summary>
    public interface IAttributeSystem
    {
        int GetLevel(uint entityId);
        int GetMind(uint entityId);
        int GetBody(uint entityId);
        int GetSpirit(uint entityId);
        int GetAttunement(uint entityId);

        int GetMaxHp(uint entityId);
        int GetCurrentHp(uint entityId);
        int GetMaxMana(uint entityId);
        int GetCurrentMana(uint entityId);
        int GetMaxStamina(uint entityId);
        int GetCurrentStamina(uint entityId);
        int GetMaxAstra(uint entityId);
        int GetCurrentAstra(uint entityId);

        void SetLevel(uint entityId, int value);
        void SetMind(uint entityId, int value);
        void SetBody(uint entityId, int value);
        void SetSpirit(uint entityId, int value);
        void SetAttunement(uint entityId, int value);

        /// <summary>Sets MaxHp; clamps CurrentHp to new MaxHp if it would exceed it.</summary>
        void SetMaxHp(uint entityId, int value);
        /// <summary>Sets CurrentHp clamped to [0, MaxHp]. No events, no persistence (INV-5).</summary>
        void SetCurrentHp(uint entityId, int value);

        void SetMaxMana(uint entityId, int value);
        void SetCurrentMana(uint entityId, int value);
        void SetMaxStamina(uint entityId, int value);
        void SetCurrentStamina(uint entityId, int value);
        void SetMaxAstra(uint entityId, int value);
        void SetCurrentAstra(uint entityId, int value);
    }
}
