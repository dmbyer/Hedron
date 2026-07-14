using System;
using System.IO;
using System.Threading.Tasks;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Account;
using Hedron.Core.Modules.BalanceInspection;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Modules.BalanceInspection.Standards
{
    /// <summary>
    /// Tier 1/4 — system-unit + YAML round-trip tests for <see cref="BalanceStandardsStore"/>.
    ///
    /// Coverage contract: docs/roadmap/completed/balance-standards-registry.md Test plan
    /// items 3–7, 12 — band-ordering structural rule, defaults fallback, structural fail-fast,
    /// mirror-drift warnings, save→load round-trip + atomicity, ability-kit shape warning.
    /// </summary>
    public sealed class BalanceStandardsStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _path;

        public BalanceStandardsStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-standards-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _path = Path.Combine(_tempDir, "standards.yaml");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private BalanceStandardsStore BuildStore(CharacterDefaultsOptions? characterDefaults = null) =>
            new(
                Microsoft.Extensions.Options.Options.Create(new BalanceOptions { StandardsPath = _path }),
                Microsoft.Extensions.Options.Options.Create(characterDefaults ?? new CharacterDefaultsOptions()),
                new AbilityRegistry());

        // ── Defaults fallback (Postcondition 3) ───────────────────────────────────

        [Fact]
        public void Missing_file_returns_compiled_defaults_with_no_warnings()
        {
            var store = BuildStore();

            var (document, warnings) = store.Load();

            Assert.Same(BalanceStandardsDefaults.Document, document);
            Assert.Empty(warnings);
        }

        // ── Band-ordering structural rule (Postcondition 4, item 3) ───────────────

        [Fact]
        public void BandSpan_not_strictly_below_third_of_tier_span_throws()
        {
            File.WriteAllText(_path, "tunables:\n  bandSpan: 999999\n");
            var store = BuildStore();

            var ex = Assert.Throws<InvalidOperationException>(() => store.Load());
            Assert.Contains("bandSpan", ex.Message);
        }

        // ── Structural fail-fast (Postcondition 4, item 5) ────────────────────────

        [Fact]
        public void Unknown_score_id_in_gear_bonuses_throws()
        {
            File.WriteAllText(_path, "cells:\n  - tier: 0\n    band: 1\n    gearBonuses:\n      notAScore: 5\n");
            var store = BuildStore();

            var ex = Assert.Throws<InvalidOperationException>(() => store.Load());
            Assert.Contains("unknown score id", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Duplicate_cell_throws()
        {
            File.WriteAllText(_path, "cells:\n  - tier: 0\n    band: 1\n  - tier: 0\n    band: 1\n");
            var store = BuildStore();

            var ex = Assert.Throws<InvalidOperationException>(() => store.Load());
            Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Out_of_range_cell_tier_throws()
        {
            File.WriteAllText(_path, "cells:\n  - tier: 99\n    band: 1\n");
            var store = BuildStore();

            var ex = Assert.Throws<InvalidOperationException>(() => store.Load());
            Assert.Contains("tier", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Negative_band_drift_tolerance_throws()
        {
            File.WriteAllText(_path, "bandDriftTolerance: -1\n");
            var store = BuildStore();

            var ex = Assert.Throws<InvalidOperationException>(() => store.Load());
            Assert.Contains("bandDriftTolerance", ex.Message);
        }

        // ── Mirror-drift warnings (Postcondition 5) ────────────────────────────────

        [Fact]
        public void Drifted_maxTier_and_referenceBaseScores_return_one_warning_each_and_do_not_throw()
        {
            File.WriteAllText(
                _path,
                "tunables:\n" +
                "  maxTier: 7\n" +
                "  referenceBaseScores:\n" +
                "    body: 999\n");
            var store = BuildStore();

            var (document, warnings) = store.Load();

            Assert.Equal(7, document.Tunables.MaxTier);
            Assert.Equal(2, warnings.Count);
            Assert.Contains(warnings, w => w.Contains("maxTier", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(warnings, w => w.Contains("referenceBaseScores", StringComparison.OrdinalIgnoreCase));
        }

        // ── Ability-kit shape (Postcondition 10) ───────────────────────────────────

        [Fact]
        public void Unknown_ability_id_in_ability_kit_warns_and_does_not_throw()
        {
            File.WriteAllText(
                _path,
                "cells:\n  - tier: 0\n    band: 1\n    abilityKit:\n      - not_a_real_ability\n");
            var store = BuildStore();

            var (_, warnings) = store.Load();

            Assert.Contains(warnings, w => w.Contains("not_a_real_ability"));
        }

        // ── Save → load round-trip + atomicity (Postcondition 8) ──────────────────

        [Fact]
        public async Task SaveAsync_then_Load_round_trips_scalar_and_dictionary_fields()
        {
            var store = BuildStore();
            var gear = new System.Collections.Generic.Dictionary<Hedron.Core.Modules.Stats.ScoreId, int>
            {
                [Hedron.Core.Modules.Stats.ScoreId.AttackPower] = 7,
            };
            var cell = new BalanceStandard(
                2, 1, new ReferenceBuildDefinition(gear, new[] { "kick" }), OutcomesOverride: null);
            var document = new BalanceStandardsDocument(
                PowerBudgetTunables.Default, BandDriftTolerance: 3,
                Outcomes: new OutcomeTolerances(0.55, 0.12, 0.7),
                Cells: new[] { cell });

            var saveResult = await store.SaveAsync(document);
            Assert.True(saveResult.Success);

            var (loaded, warnings) = store.Load();

            Assert.Empty(warnings);
            Assert.Equal(3, loaded.BandDriftTolerance);
            Assert.Equal(0.55, loaded.Outcomes.EqualCellWinRate);
            var loadedCell = Assert.Single(loaded.Cells);
            Assert.Equal(2, loadedCell.Tier);
            Assert.Equal(1, loadedCell.Band);
            Assert.Equal(7, loadedCell.ReferenceBuild.GearBonuses[Hedron.Core.Modules.Stats.ScoreId.AttackPower]);
            Assert.Equal("kick", Assert.Single(loadedCell.ReferenceBuild.AbilityKit));
        }

        [Fact]
        public async Task SaveAsync_writes_atomically_with_no_leftover_tmp_file()
        {
            var store = BuildStore();
            var document = new BalanceStandardsDocument(
                PowerBudgetTunables.Default, BandDriftTolerance: 1,
                Outcomes: BalanceStandardsDefaults.Outcomes, Cells: Array.Empty<BalanceStandard>());

            await store.SaveAsync(document);

            Assert.True(File.Exists(_path));
            Assert.False(File.Exists(_path + ".tmp"));
        }

        [Fact]
        public async Task SaveAsync_refuses_and_writes_nothing_on_structural_failure()
        {
            var store = BuildStore();
            var invalid = new BalanceStandardsDocument(
                PowerBudgetTunables.Default, BandDriftTolerance: -1,
                Outcomes: BalanceStandardsDefaults.Outcomes, Cells: Array.Empty<BalanceStandard>());

            var result = await store.SaveAsync(invalid);

            Assert.False(result.Success);
            Assert.NotEmpty(result.Errors);
            Assert.False(File.Exists(_path));
        }
    }
}
