using System.Collections.Generic;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// The fully-resolved shape of one <see cref="CombatantSpec"/> — scores, ability kit, tier,
    /// and (when the source carries one) the (Tier, Band) verdict cell — read once per scenario
    /// from disk/registry (<see cref="ISimCombatantFactory.Resolve"/>) so the per-run hot path
    /// does no file/registry I/O, then stamped into a fresh <see cref="SandboxWorld"/> per run
    /// (<see cref="ISimCombatantFactory.Materialize"/>).
    /// </summary>
    public sealed record ResolvedCombatant(
        string Name,
        IReadOnlyDictionary<ScoreId, int> Scores,
        IReadOnlyList<string> AbilityKit,
        int Tier,
        string PolicyId,
        PowerBand? Cell);

    /// <summary>
    /// Two-phase combatant resolution: <see cref="Resolve"/> reads the mob-template catalog or the
    /// balance-standards registry once per scenario (never per run); <see cref="Materialize"/>
    /// stamps a <see cref="ResolvedCombatant"/> into a specific run's <see cref="SandboxWorld"/>.
    /// Fails fast on an unresolvable mob-template id, unknown ability id, or undefined score id.
    /// </summary>
    public interface ISimCombatantFactory
    {
        /// <summary>Resolves a scenario-authored spec into scores/ability-kit/tier/cell. No I/O per run.</summary>
        ResolvedCombatant Resolve(CombatantSpec spec);

        /// <summary>Stamps a resolved combatant into <paramref name="world"/>, returning its entity id.</summary>
        uint Materialize(SandboxWorld world, ResolvedCombatant resolved);
    }
}
