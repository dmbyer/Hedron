namespace Hedron.Core.Modules.Stats.Systems
{
    /// <summary>
    /// Aggregation seam for effective entity scores — the only place a raw attribute becomes an
    /// effective one. Reads base values from <see cref="Modules.Attributes.Systems.IAttributeSystem"/>
    /// and folds every active modifier (effects and contributors: gear, passives, progression, tier)
    /// to produce ready-to-use values for combat, <c>score</c>, the prompt and the relative-power read.
    /// INV-5: never publishes events or calls persistence. Pure aggregation only; no setters.
    /// </summary>
    public interface IStatSystem
    {
        /// <summary>Effective Mind; equals <see cref="Get"/>(Mind).</summary>
        int GetEffectiveMind(uint entityId);
        /// <summary>Effective Body; equals <see cref="Get"/>(Body).</summary>
        int GetEffectiveBody(uint entityId);
        /// <summary>Effective Spirit; equals <see cref="Get"/>(Spirit).</summary>
        int GetEffectiveSpirit(uint entityId);
        /// <summary>Effective Attunement; equals <see cref="Get"/>(Attunement).</summary>
        int GetEffectiveAttunement(uint entityId);

        /// <summary>
        /// Effective attack power; equals <see cref="Get"/>(AttackPower): effective Body / 2 plus
        /// every AttackPower modifier (worn gear included), floored at 0.
        /// </summary>
        int GetEffectiveAttackPower(uint entityId);

        /// <summary>
        /// Effective defense; equals <see cref="Get"/>(Defense): effective Body / 4 plus every Defense
        /// modifier (armor included), floored at 0. Defense governance is interim; a dedicated
        /// evasion/armor score lands in a later slice.
        /// </summary>
        int GetEffectiveDefense(uint entityId);

        /// <summary>Clamped current HP; equals <see cref="Get"/>(HpCurrent).</summary>
        int GetCurrentHp(uint entityId);
        /// <summary>Effective max HP; equals <see cref="Get"/>(HpMax).</summary>
        int GetMaxHp(uint entityId);

        /// <summary>
        /// Generalized score read. Returns the effective value for any <see cref="ScoreId"/>; the
        /// typed getters are thin wrappers over it. Pool max/current delegate to the pool-cap seam on
        /// <see cref="Modules.Attributes.Systems.IAttributeSystem"/>, so display and writes share one cap.
        /// </summary>
        int Get(uint entityId, ScoreId score);
    }
}
