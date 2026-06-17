namespace Hedron.Core.Modules.Stats.Systems
{
    /// <summary>
    /// Aggregation seam for effective entity scores. Reads from <see cref="Modules.Attributes.Systems.IAttributeSystem"/>
    /// and equipment to produce ready-to-use values for combat and future consumers.
    /// INV-5: never publishes events or calls persistence. Pure aggregation only.
    /// </summary>
    public interface IStatSystem
    {
        int GetEffectiveMind(uint entityId);
        int GetEffectiveBody(uint entityId);
        int GetEffectiveSpirit(uint entityId);
        int GetEffectiveAttunement(uint entityId);

        /// <summary>
        /// Base attack power (Body / 2) only. Worn-gear bonuses ride the effect contributor and are
        /// folded by <see cref="Get"/>(AttackPower) — read that for the gear-inclusive value.
        /// </summary>
        int GetEffectiveAttackPower(uint entityId);

        /// <summary>
        /// Base defense (Body / 4) only. Armor bonuses ride the effect contributor and are folded by
        /// <see cref="Get"/>(Defense). Defense governance is interim; a dedicated evasion/armor score
        /// lands in a later slice.
        /// </summary>
        int GetEffectiveDefense(uint entityId);

        int GetCurrentHp(uint entityId);
        int GetMaxHp(uint entityId);

        /// <summary>
        /// Generalized score read. Returns the effective value for any <see cref="ScoreId"/>.
        /// Typed getters are thin wrappers over this seam so existing call sites are untouched.
        /// In this slice the value is base only; S2 (effect substrate) will sum StatModifiers
        /// inside this method with no interface change.
        /// </summary>
        int Get(uint entityId, ScoreId score);
    }
}
