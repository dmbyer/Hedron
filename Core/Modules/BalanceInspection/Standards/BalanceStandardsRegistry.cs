using System.Collections.Generic;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection.Standards
{
    /// <summary>
    /// Default <see cref="IBalanceStandardsRegistry"/>. Dense-fills the document's sparse-authored
    /// <see cref="BalanceStandardsDocument.Cells"/> at construction so every (Tier, Band) lookup in
    /// range always resolves (seed OQ4) — missing cells get an empty-gear reference build and no
    /// outcomes override, so <see cref="OutcomesFor"/> falls through to the document's global
    /// tolerances for them.
    /// </summary>
    public sealed class BalanceStandardsRegistry : DefinitionRegistry<PowerBand, BalanceStandard>, IBalanceStandardsRegistry
    {
        private static readonly ReferenceBuildDefinition EmptyReferenceBuild =
            new(new Dictionary<ScoreId, int>(), System.Array.Empty<string>());

        private readonly OutcomeTolerances _globalOutcomes;

        public PowerBudgetTunables Tunables { get; }
        public int BandDriftTolerance { get; }

        public BalanceStandardsRegistry(BalanceStandardsDocument document)
            : base(BuildDenseCells(document), cell => new PowerBand(cell.Tier, cell.Band))
        {
            Tunables = document.Tunables;
            BandDriftTolerance = document.BandDriftTolerance;
            _globalOutcomes = document.Outcomes;
        }

        public OutcomeTolerances OutcomesFor(int tier, int band)
            => TryGet(new PowerBand(tier, band), out var cell) && cell.OutcomesOverride is not null
                ? cell.OutcomesOverride
                : _globalOutcomes;

        public PowerSnapshot ReferenceSnapshot(int tier, int band)
        {
            var scores = new Dictionary<ScoreId, int>(Tunables.ReferenceBaseScores);

            if (TryGet(new PowerBand(tier, band), out var cell))
            {
                foreach (var (score, bonus) in cell.ReferenceBuild.GearBonuses)
                    scores[score] = scores.TryGetValue(score, out var existing) ? existing + bonus : bonus;
            }

            return new PowerSnapshot(scores);
        }

        private static IEnumerable<BalanceStandard> BuildDenseCells(BalanceStandardsDocument document)
        {
            var authored = new Dictionary<PowerBand, BalanceStandard>();
            foreach (var cell in document.Cells)
                authored[new PowerBand(cell.Tier, cell.Band)] = cell;

            for (var tier = 0; tier <= document.Tunables.MaxTier; tier++)
            {
                for (var band = 1; band <= document.Tunables.BandsPerTier; band++)
                {
                    var key = new PowerBand(tier, band);
                    yield return authored.TryGetValue(key, out var cell)
                        ? cell
                        : new BalanceStandard(tier, band, EmptyReferenceBuild, OutcomesOverride: null);
                }
            }
        }
    }
}
