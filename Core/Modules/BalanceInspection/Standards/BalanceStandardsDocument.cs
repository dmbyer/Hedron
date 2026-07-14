using System.Collections.Generic;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection.Standards
{
    /// <summary>
    /// The canonical loadout a reference build at a (Tier, Band) cell carries: gear-equivalent
    /// stat bonuses (added to <see cref="PowerBudgetTunables.ReferenceBaseScores"/> to form the
    /// cell's <see cref="IBalanceStandardsRegistry.ReferenceSnapshot"/>), plus a shaped-but-inert
    /// ability-kit field (seed OQ2) — validated for shape (unknown id → load warning) but consumed
    /// by nothing until a later slice activates it, without a schema break.
    /// </summary>
    public sealed record ReferenceBuildDefinition(
        IReadOnlyDictionary<ScoreId, int> GearBonuses,
        IReadOnlyList<string> AbilityKit);

    /// <summary>
    /// Expected-outcome tolerances for a combat comparison — inert until the sim engine (sim-2)
    /// compares expected-vs-actual. Global defaults live on <see cref="BalanceStandardsDocument.Outcomes"/>;
    /// a cell may optionally override via <see cref="BalanceStandard.OutcomesOverride"/>.
    /// </summary>
    public sealed record OutcomeTolerances(
        double EqualCellWinRate,
        double WinRateTolerance,
        double HigherBandWinRateFloor);

    /// <summary>
    /// One authored (Tier, Band) cell. Target power ranges are never authored here (seed OQ1) —
    /// they remain a pure derivation of <see cref="BalanceStandardsDocument.Tunables"/> via
    /// <see cref="IPowerBudgetSystem.TargetRange"/>.
    /// </summary>
    public sealed record BalanceStandard(
        int Tier,
        int Band,
        ReferenceBuildDefinition ReferenceBuild,
        OutcomeTolerances? OutcomesOverride);

    /// <summary>
    /// The balance-standards document (Spine F registry backing store): the oracle's
    /// <see cref="PowerBudgetTunables"/>, the band-drift audit tolerance (seed OQ7), the global
    /// expected-outcome tolerances, and the sparse-authored cell table (dense-filled by
    /// <see cref="BalanceStandardsRegistry"/> — seed OQ4). Immutable; a save produces a new
    /// document rather than mutating this one.
    /// </summary>
    public sealed record BalanceStandardsDocument(
        PowerBudgetTunables Tunables,
        int BandDriftTolerance,
        OutcomeTolerances Outcomes,
        IReadOnlyList<BalanceStandard> Cells);
}
