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

        /// <summary>Body / 2 + MainHand item DamageBonus (0 if no weapon or no bonus).</summary>
        int GetEffectiveAttackPower(uint entityId);

        /// <summary>Body / 4. Defense governance is interim; evasion/armor score lands in a later slice.</summary>
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
