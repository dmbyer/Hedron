namespace Hedron.Core.Modules.Stats.Systems
{
    /// <summary>
    /// Aggregation seam for effective entity stats. Reads from <see cref="Modules.Attributes.Systems.IAttributeSystem"/>
    /// and equipment components to produce ready-to-use values for combat and future consumers.
    /// INV-5: never publishes events or calls persistence. Pure aggregation only.
    /// </summary>
    public interface IStatSystem
    {
        int GetEffectiveStrength(uint entityId);
        int GetEffectiveDexterity(uint entityId);
        int GetEffectiveConstitution(uint entityId);

        /// <summary>Strength / 2 + MainHand item DamageBonus (0 if no weapon or no bonus).</summary>
        int GetEffectiveAttackPower(uint entityId);

        /// <summary>Dexterity / 4. Armor-slot bonus deferred to a future slice.</summary>
        int GetEffectiveDefense(uint entityId);

        int GetCurrentHp(uint entityId);
        int GetMaxHp(uint entityId);
    }
}
