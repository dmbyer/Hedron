namespace Hedron.Core.Modules.Aspects.Systems
{
    /// <summary>
    /// Core system: generic aspect math with no game-semantic branching (no FireSystem,
    /// no per-aspect switch). Three responsibilities:
    /// <list type="bullet">
    ///   <item><description><see cref="Resolve"/> — apply affinity boost + independent resist to produce the final magnitude.</description></item>
    ///   <item><description><see cref="Affinity"/> — return the entity's outgoing aspect composition (its "identity").</description></item>
    ///   <item><description><see cref="Resist"/> — return the entity's effective resistance to a specific aspect (compute-on-read, INV-24).</description></item>
    /// </list>
    /// Pure: no events, no persistence, no game rules (INV-2, INV-5).
    /// </summary>
    public interface IAspectSystem
    {
        /// <summary>
        /// Applies <paramref name="composition"/>'s affinity boost (attacker) and per-aspect
        /// resist (defender) to <paramref name="magnitude"/>. Returns the final integer damage
        /// to apply via <c>IAttributeSystem.SetCurrentHp</c>.
        /// Formula per aspect A present in composition:
        ///   portion = magnitude * weight / 100
        ///   boosted = portion * (1 + affinityBoost_A)   // attacker boost from AspectAffinitiesComponent
        ///   resisted = boosted * (1 - resist_A / 100)    // defender resist clamped [0,100]
        /// The results across all aspects are summed and clamped to [0, int.MaxValue].
        /// When composition is empty the magnitude is returned unchanged.
        /// </summary>
        int Resolve(int magnitude, AspectComposition composition, uint attackerEntityId, uint defenderEntityId);

        /// <summary>
        /// Returns the entity's outgoing aspect composition (affinity/identity). Reads
        /// <c>AspectAffinitiesComponent.AffinityWeights</c> directly — no cached value.
        /// Returns <see cref="AspectComposition.Empty"/> when the component is absent.
        /// </summary>
        AspectComposition Affinity(uint entityId);

        /// <summary>
        /// Returns the entity's effective resistance to <paramref name="aspect"/> as an
        /// integer in [0, 100] (100 = full immunity). Folds the base value from
        /// <c>AspectAffinitiesComponent.BaseResistances</c> with any registered
        /// <c>IAspectResistContributor</c> modifiers — compute-on-read, never cached (INV-24).
        /// Returns 0 when the component is absent or the aspect has no entry.
        /// </summary>
        int Resist(uint entityId, AspectId aspect);
    }
}
