using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.Stats;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>
    /// Tier 1 — <see cref="SimScenarioStore"/> load + fail-fast structural validation
    /// (Postcondition 2) and editor save/list (Postcondition 7).
    /// </summary>
    public sealed class SimScenarioStoreTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "hedron-sim-" + Guid.NewGuid().ToString("N"));
        private readonly string _scenarioDir = Path.Combine(Path.GetTempPath(), "hedron-sim-scenarios-" + Guid.NewGuid().ToString("N"));

        public SimScenarioStoreTests() => Directory.CreateDirectory(_tempDir);

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
            try { Directory.Delete(_scenarioDir, recursive: true); } catch { /* best-effort */ }
        }

        private SimScenarioStore NewStore() =>
            new(new ISimCombatantPolicy[] { new MeleeOnlyPolicy(), new RoundRobinPolicy(), new CooldownFirstPolicy(new AbilityRegistry()) },
                Options.Create(new SimulationOptions { ScenarioDirectory = _scenarioDir }));

        private string WriteScenario(string body)
        {
            var path = Path.Combine(_tempDir, "scenario.yaml");
            File.WriteAllText(path, body);
            return path;
        }

        private const string ValidScenario = """
            kind: Combat
            name: test-scenario
            seed: 5
            iterations: 10
            maxTicksPerRun: 20
            sides:
              - combatants:
                  - source: Inline
                    policyId: melee-only
                    tier: 1
                    band: 1
                    inline:
                      scores:
                        body: 10
                        hpMax: 100
              - combatants:
                  - source: Inline
                    policyId: melee-only
                    inline:
                      scores:
                        body: 10
                        hpMax: 100
            """;

        // ── Valid load ────────────────────────────────────────────────────────

        [Fact]
        public void Load_ValidScenario_ReturnsExpectedFields()
        {
            var store = NewStore();
            var path = WriteScenario(ValidScenario);

            var scenario = store.Load(path);

            Assert.Equal(ScenarioKind.Combat, scenario.Kind);
            Assert.Equal("test-scenario", scenario.Name);
            Assert.Equal(5, scenario.Seed);
            Assert.Equal(10, scenario.Iterations);
            Assert.Equal(20, scenario.MaxTicksPerRun);
            Assert.Equal(2, scenario.Sides.Count);
            Assert.Single(scenario.Sides[0].Combatants);
            var combatant = scenario.Sides[0].Combatants[0];
            Assert.Equal(CombatantSourceKind.Inline, combatant.Source);
            Assert.Equal("melee-only", combatant.PolicyId);
            Assert.Equal(1, combatant.Tier);
            Assert.Equal(1, combatant.Band);
            Assert.Equal(10, combatant.Inline!.Scores[ScoreId.Body]);
            Assert.Equal(100, combatant.Inline!.Scores[ScoreId.HpMax]);
        }

        [Fact]
        public void Load_SeedOverride_ReplacesFileSeed()
        {
            var store = NewStore();
            var path = WriteScenario(ValidScenario);

            var scenario = store.Load(path, seedOverride: 999);

            Assert.Equal(999, scenario.Seed);
        }

        // ── Fail-fast structural violations ──────────────────────────────────

        [Fact]
        public void Load_UnknownKind_Throws()
        {
            var store = NewStore();
            var path = WriteScenario(ValidScenario.Replace("kind: Combat", "kind: NotAKind"));
            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_UnknownPolicyId_Throws()
        {
            var store = NewStore();
            var path = WriteScenario(ValidScenario.Replace("policyId: melee-only", "policyId: not-a-policy"));
            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_EmptySide_Throws()
        {
            var store = NewStore();
            const string body = """
                kind: Combat
                name: bad
                seed: 1
                iterations: 1
                maxTicksPerRun: 1
                sides:
                  - combatants: []
                  - combatants:
                      - source: Inline
                        policyId: melee-only
                        inline:
                          scores:
                            body: 10
                """;
            var path = WriteScenario(body);
            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_TwoCombatantsOnASide_Throws()
        {
            var store = NewStore();
            const string body = """
                kind: Combat
                name: bad
                seed: 1
                iterations: 1
                maxTicksPerRun: 1
                sides:
                  - combatants:
                      - source: Inline
                        policyId: melee-only
                        inline:
                          scores:
                            body: 10
                      - source: Inline
                        policyId: melee-only
                        inline:
                          scores:
                            body: 10
                  - combatants:
                      - source: Inline
                        policyId: melee-only
                        inline:
                          scores:
                            body: 10
                """;
            var path = WriteScenario(body);
            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_NonPositiveIterations_Throws()
        {
            var store = NewStore();
            var path = WriteScenario(ValidScenario.Replace("iterations: 10", "iterations: 0"));
            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_NonPositiveMaxTicks_Throws()
        {
            var store = NewStore();
            var path = WriteScenario(ValidScenario.Replace("maxTicksPerRun: 20", "maxTicksPerRun: 0"));
            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_UnknownSourceDiscriminator_Throws()
        {
            var store = NewStore();
            var path = WriteScenario(ValidScenario.Replace("source: Inline", "source: NotASource"));
            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_MissingFile_Throws()
        {
            var store = NewStore();
            Assert.Throws<FileNotFoundException>(() => store.Load(Path.Combine(_tempDir, "missing.yaml")));
        }

        // ── progressionRate (sim-4) ──────────────────────────────────────────

        private const string ValidProgressionScenario = """
            kind: ProgressionRate
            name: test-progression-scenario
            seed: 5
            iterations: 10
            maxTicksPerRun: 1
            progression:
              targetTrack: body
              targetImprovements: 3
              maxKillsPerRun: 100
              ticksPerKill: 5.5
            sides:
              - combatants:
                  - source: ReferenceBuild
                    tier: 1
                    band: 1
              - combatants:
                  - source: ReferenceBuild
                    tier: 1
                    band: 1
            """;

        [Fact]
        public void Load_ValidProgressionScenario_RoundTripsKindAndSettings_PolicyIdNotRequired()
        {
            var store = NewStore();
            var path = WriteScenario(ValidProgressionScenario);

            var scenario = store.Load(path);

            Assert.Equal(ScenarioKind.ProgressionRate, scenario.Kind);
            Assert.NotNull(scenario.Progression);
            Assert.Equal(ScoreId.Body, scenario.Progression!.TargetTrack);
            Assert.Equal(3, scenario.Progression.TargetImprovements);
            Assert.Equal(100, scenario.Progression.MaxKillsPerRun);
            Assert.Equal(5.5, scenario.Progression.TicksPerKill);
            Assert.Equal(string.Empty, scenario.Sides[0].Combatants[0].PolicyId);
        }

        [Fact]
        public void Load_ProgressionScenario_MissingProgressionSection_Throws()
        {
            var store = NewStore();
            var body = ValidProgressionScenario.Replace(
                """
                progression:
                  targetTrack: body
                  targetImprovements: 3
                  maxKillsPerRun: 100
                  ticksPerKill: 5.5
                """, "");
            var path = WriteScenario(body);

            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_ProgressionSectionOnCombatScenario_Throws()
        {
            var store = NewStore();
            var path = WriteScenario(ValidProgressionScenario.Replace("kind: ProgressionRate", "kind: Combat"));

            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_ProgressionUntrackedTargetTrack_Throws()
        {
            var store = NewStore();
            var path = WriteScenario(ValidProgressionScenario.Replace("targetTrack: body", "targetTrack: mind"));

            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_ProgressionNonPositiveTargetImprovements_Throws()
        {
            var store = NewStore();
            var path = WriteScenario(ValidProgressionScenario.Replace("targetImprovements: 3", "targetImprovements: 0"));

            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_ProgressionNonPositiveMaxKillsPerRun_Throws()
        {
            var store = NewStore();
            var path = WriteScenario(ValidProgressionScenario.Replace("maxKillsPerRun: 100", "maxKillsPerRun: 0"));

            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_ProgressionNonPositiveTicksPerKill_Throws()
        {
            var store = NewStore();
            var path = WriteScenario(ValidProgressionScenario.Replace("ticksPerKill: 5.5", "ticksPerKill: 0"));

            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public void Load_ProgressionScenario_SideCountNotTwo_Throws()
        {
            var store = NewStore();
            const string body = """
                kind: ProgressionRate
                name: bad
                seed: 1
                iterations: 1
                maxTicksPerRun: 1
                progression:
                  targetTrack: body
                  targetImprovements: 1
                  maxKillsPerRun: 10
                sides:
                  - combatants:
                      - source: ReferenceBuild
                        tier: 1
                        band: 1
                """;
            var path = WriteScenario(body);

            Assert.Throws<InvalidOperationException>(() => store.Load(path));
        }

        [Fact]
        public async Task SaveAsync_ValidProgressionScenario_RoundTripsSettings()
        {
            var store = NewStore();
            var definition = new ScenarioDefinition(
                ScenarioKind.ProgressionRate, "progression-editor-scenario", Seed: 5, Iterations: 10, MaxTicksPerRun: 1,
                Sides: new[]
                {
                    new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, string.Empty, Tier: 1, Band: 1) }),
                    new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, string.Empty, Tier: 1, Band: 1) }),
                },
                Progression: new ProgressionSettings(ScoreId.Body, TargetImprovements: 3, MaxKillsPerRun: 100, TicksPerKill: 5.5));

            var path = await store.SaveAsync(definition);
            var reloaded = store.Load(path);

            Assert.Equal(ScenarioKind.ProgressionRate, reloaded.Kind);
            Assert.Equal(ScoreId.Body, reloaded.Progression!.TargetTrack);
            Assert.Equal(3, reloaded.Progression.TargetImprovements);
            Assert.Equal(100, reloaded.Progression.MaxKillsPerRun);
            Assert.Equal(5.5, reloaded.Progression.TicksPerKill);
        }

        // ── Save / List (Postcondition 7) ────────────────────────────────────

        private static ScenarioDefinition ValidDefinition(string name = "editor-scenario", int seed = 5) => new(
            ScenarioKind.Combat, name, seed, Iterations: 10, MaxTicksPerRun: 20,
            Sides: new[]
            {
                new ScenarioSide(new[] { new CombatantSpec(
                    CombatantSourceKind.Inline, "melee-only", Tier: 1, Band: 1,
                    Inline: new InlineStatBlock(
                        new System.Collections.Generic.Dictionary<ScoreId, int> { [ScoreId.Body] = 10, [ScoreId.HpMax] = 100 },
                        new System.Collections.Generic.List<string>())) }),
                new ScenarioSide(new[] { new CombatantSpec(
                    CombatantSourceKind.Inline, "melee-only",
                    Inline: new InlineStatBlock(
                        new System.Collections.Generic.Dictionary<ScoreId, int> { [ScoreId.Body] = 10, [ScoreId.HpMax] = 100 },
                        new System.Collections.Generic.List<string>())) }),
            });

        [Fact]
        public async Task SaveAsync_ValidScenario_WritesAtomicallyAndLoadRoundTripsFieldEqualDefinition()
        {
            var store = NewStore();
            var definition = ValidDefinition();

            var path = await store.SaveAsync(definition);

            Assert.Empty(Directory.EnumerateFiles(_scenarioDir, "*.tmp"));
            Assert.True(File.Exists(path));

            var reloaded = store.Load(path);
            Assert.Equal(definition.Kind, reloaded.Kind);
            Assert.Equal(definition.Name, reloaded.Name);
            Assert.Equal(definition.Seed, reloaded.Seed);
            Assert.Equal(definition.Iterations, reloaded.Iterations);
            Assert.Equal(definition.MaxTicksPerRun, reloaded.MaxTicksPerRun);
            Assert.Equal(2, reloaded.Sides.Count);
            Assert.Equal(1, reloaded.Sides[0].Combatants[0].Tier);
            Assert.Equal(1, reloaded.Sides[0].Combatants[0].Band);
            Assert.Equal(10, reloaded.Sides[0].Combatants[0].Inline!.Scores[ScoreId.Body]);
            Assert.Equal(100, reloaded.Sides[0].Combatants[0].Inline!.Scores[ScoreId.HpMax]);
        }

        [Fact]
        public async Task SaveAsync_InvalidScenario_ThrowsWithNamedErrorsAndWritesNothing()
        {
            var store = NewStore();
            var invalid = ValidDefinition() with { Iterations = 0 };

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(invalid));

            Assert.False(Directory.Exists(_scenarioDir) && Directory.EnumerateFileSystemEntries(_scenarioDir).Any());
        }

        [Fact]
        public async Task List_ReturnsSavedFiles()
        {
            var store = NewStore();
            await store.SaveAsync(ValidDefinition("scenario-one"));
            await store.SaveAsync(ValidDefinition("scenario-two"));

            var summaries = store.List();

            Assert.Equal(2, summaries.Count);
            Assert.Contains(summaries, s => s.Name == "scenario-one");
            Assert.Contains(summaries, s => s.Name == "scenario-two");
        }

        [Fact]
        public async Task SaveAsync_SameName_UpsertsRatherThanDuplicating()
        {
            var store = NewStore();
            await store.SaveAsync(ValidDefinition("same-name", seed: 1));
            await store.SaveAsync(ValidDefinition("same-name", seed: 2));

            var summaries = store.List();

            Assert.Single(summaries);
            var reloaded = store.Load(summaries[0].Path);
            Assert.Equal(2, reloaded.Seed);
        }
    }
}
