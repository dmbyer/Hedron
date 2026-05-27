namespace Hedron.Core.Modules.Attributes.Systems
{
    /// <summary>
    /// Read/write seam for <see cref="ECS.Components.AttributesComponent"/> and
    /// <see cref="ECS.Components.PoolsComponent"/>. Getters are the surface the combat slice
    /// will call; setters serve the admin and initialization paths.
    /// INV-5: this system never touches the event bus or persistence.
    /// </summary>
    public interface IAttributeSystem
    {
        int GetLevel(uint entityId);
        int GetStrength(uint entityId);
        int GetDexterity(uint entityId);
        int GetConstitution(uint entityId);
        int GetMaxHp(uint entityId);
        int GetCurrentHp(uint entityId);

        void SetLevel(uint entityId, int value);
        void SetStrength(uint entityId, int value);
        void SetDexterity(uint entityId, int value);
        void SetConstitution(uint entityId, int value);
        /// <summary>
        /// Sets MaxHp and clamps CurrentHp to the new MaxHp if it would exceed it (INV-8).
        /// </summary>
        void SetMaxHp(uint entityId, int value);

        /// <summary>
        /// Sets CurrentHp, clamped to [0, MaxHp]. Game rule enforced here (INV-8). No events, no persistence (INV-5).
        /// </summary>
        void SetCurrentHp(uint entityId, int value);
    }
}
