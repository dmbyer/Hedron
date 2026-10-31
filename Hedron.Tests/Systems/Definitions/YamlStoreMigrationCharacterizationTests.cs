using System;
using System.IO;
using System.Threading.Tasks;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Account;
using Hedron.Core.Modules.BalanceInspection;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Systems.Definitions
{
    /// <summary>
    /// T3b — characterization of the three hand-rolled YAML stores' failure and serialization
    /// behavior, written and green <em>before</em> they migrated onto the shared
    /// <c>Core/Systems/Definitions/</c> store (help-topics WP1, R15). The goldens were captured from
    /// the pre-migration stores; a migration that changes an exception type, a message, or the
    /// written YAML fails here even though the existing suites never exercised those paths.
    /// </summary>
    public sealed class YamlStoreMigrationCharacterizationTests : IDisposable
    {
        private const string MalformedYaml = "tunables: [unclosed\n  : : :";

        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "hedron-yaml-char-" + Guid.NewGuid().ToString("N"));

        public YamlStoreMigrationCharacterizationTests() => Directory.CreateDirectory(_tempDir);

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }

        private string StandardsPath => Path.Combine(_tempDir, "standards.yaml");

        private BalanceStandardsStore Standards() => new(
            Options.Create(new BalanceOptions { StandardsPath = StandardsPath }),
            Options.Create(new CharacterDefaultsOptions()),
            new AbilityRegistry());

        private SimScenarioStore Scenarios(string? directory = null) => new(
            new ISimCombatantPolicy[] { new MeleeOnlyPolicy() },
            Options.Create(new SimulationOptions { ScenarioDirectory = directory ?? Path.Combine(_tempDir, "scenarios") }));

        private static string Normalize(string text) => text.Replace("\r\n", "\n");

        // ── Balance standards ─────────────────────────────────────────────────

        [Fact]
        public void Standards_malformed_yaml_throws_the_original_YamlDotNet_exception()
        {
            File.WriteAllText(StandardsPath, MalformedYaml);

            var ex = Assert.ThrowsAny<Exception>(() => Standards().Load());

            Assert.Equal("YamlDotNet.Core.YamlException", ex.GetType().FullName);
            Assert.StartsWith("No node deserializer was able to deserialize the node into type", ex.Message);
        }

        [Fact]
        public void Standards_empty_file_loads_a_defaults_equivalent_document()
        {
            File.WriteAllText(StandardsPath, "");

            var (document, warnings) = Standards().Load();

            Assert.NotSame(BalanceStandardsDefaults.Document, document);
            Assert.Equal(BalanceStandardsDefaults.Document.BandDriftTolerance, document.BandDriftTolerance);
            Assert.Empty(document.Cells);
            Assert.Empty(warnings);
        }

        [Fact]
        public async Task Standards_save_writes_the_pre_migration_yaml()
        {
            await Standards().SaveAsync(BalanceStandardsDefaults.Document);

            Assert.Equal(StandardsGolden, Normalize(File.ReadAllText(StandardsPath)));
        }

        // ── Sim scenarios ─────────────────────────────────────────────────────

        [Fact]
        public void Scenario_missing_file_throws_FileNotFound_with_todays_message()
        {
            var path = Path.Combine(_tempDir, "absent.yaml");

            var ex = Assert.Throws<FileNotFoundException>(() => Scenarios().Load(path));

            Assert.Equal($"scenario file not found at '{path}'.", ex.Message);
        }

        [Fact]
        public void Scenario_malformed_yaml_throws_the_original_YamlDotNet_exception()
        {
            var path = Path.Combine(_tempDir, "bad.yaml");
            File.WriteAllText(path, MalformedYaml);

            var ex = Assert.ThrowsAny<Exception>(() => Scenarios().Load(path));

            Assert.Equal("YamlDotNet.Core.SemanticErrorException", ex.GetType().FullName);
            Assert.Equal("While parsing a flow sequence, did not find expected ',' or ']'.", ex.Message);
        }

        [Fact]
        public void Scenario_empty_file_throws_InvalidOperation_scenario_yaml_is_empty()
        {
            var path = Path.Combine(_tempDir, "empty.yaml");
            File.WriteAllText(path, "");

            var ex = Assert.Throws<InvalidOperationException>(() => Scenarios().Load(path));

            Assert.Equal("scenario YAML is empty.", ex.Message);
        }

        [Fact]
        public void Scenario_list_keeps_an_unparseable_file_under_its_filename_name()
        {
            var directory = Path.Combine(_tempDir, "listed");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "broken.yaml"), MalformedYaml);

            var summary = Assert.Single(Scenarios(directory).List());

            Assert.Equal("broken", summary.Name);
            Assert.Equal("broken.yaml", summary.FileName);
        }

        [Fact]
        public async Task Scenario_save_writes_the_pre_migration_yaml_and_sanitized_file_name()
        {
            var source = Path.Combine(_tempDir, "src.yaml");
            File.WriteAllText(source, ScenarioSource);
            var store = Scenarios();

            var path = await store.SaveAsync(store.Load(source));

            Assert.Equal("golden-scenario.yaml", Path.GetFileName(path));
            Assert.Equal(ScenarioGolden, Normalize(File.ReadAllText(path)));
        }

        // ── World manifest ────────────────────────────────────────────────────

        [Fact]
        public async Task Manifest_write_omits_nulls_as_before()
        {
            var store = new WorldManifestStore(Options.Create(new WorldOptions { ContentDirectory = _tempDir }));

            await store.WriteAsync(new WorldManifest(1, null));

            Assert.Equal("schemaVersion: 1\n", Normalize(File.ReadAllText(store.FilePath)));
        }

        // ── Goldens (captured from the pre-migration stores) ──────────────────

        private const string ScenarioSource =
            "kind: Combat\nname: golden scenario\nseed: 5\niterations: 10\nmaxTicksPerRun: 20\nsides:\n" +
            "  - combatants:\n      - source: Inline\n        policyId: melee-only\n        inline:\n          scores:\n            body: 10\n            hpMax: 100\n" +
            "  - combatants:\n      - source: Inline\n        policyId: melee-only\n        inline:\n          scores:\n            body: 10\n            hpMax: 100\n";

        private const string ScenarioSideGolden =
            "- combatants:\n" +
            "  - source: Inline\n" +
            "    policyId: melee-only\n" +
            "    mobBlueprintId: \n" +
            "    tier: \n" +
            "    band: \n" +
            "    inline:\n" +
            "      scores:\n" +
            "        body: 10\n" +
            "        hpMax: 100\n" +
            "      abilityKit: []\n";

        private const string ScenarioGolden =
            "kind: Combat\nname: golden scenario\nseed: 5\niterations: 10\nmaxTicksPerRun: 20\nsides:\n" +
            ScenarioSideGolden + ScenarioSideGolden +
            "progression: \n";

        private const string StandardsGolden =
            "tunables:\n" +
            "  weights:\n" +
            "    mind: 1\n    body: 10\n    spirit: 1\n    attunement: 1\n    hpmax: 2\n    manamax: 0\n    staminamax: 0\n    astramax: 0\n" +
            "    hpcurrent: 0\n    manacurrent: 0\n    staminacurrent: 0\n    astracurrent: 0\n    attackpower: 8\n    defense: 8\n" +
            "  bandSpan: 20\n" +
            "  bandsPerTier: 3\n" +
            "  referenceBaseScores:\n" +
            "    mind: 10\n    body: 10\n    spirit: 10\n    attunement: 10\n    hpmax: 100\n    manamax: 50\n    staminamax: 50\n    astramax: 10\n" +
            "    attackpower: 5\n    defense: 2\n" +
            "  maxTier: 6\n" +
            "  tierBaselineStep: 10\n" +
            "  trackedScores:\n  - body\n  - hpmax\n" +
            "bandDriftTolerance: 1\n" +
            "outcomes:\n  equalCellWinRate: 0.5\n  winRateTolerance: 0.1\n  higherBandWinRateFloor: 0.65\n" +
            "cells: []\n";
    }
}
