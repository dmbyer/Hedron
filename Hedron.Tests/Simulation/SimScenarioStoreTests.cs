using System;
using System.IO;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.Stats;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>
    /// Tier 1 — <see cref="SimScenarioStore"/> load + fail-fast structural validation
    /// (Postcondition 2).
    /// </summary>
    public sealed class SimScenarioStoreTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "hedron-sim-" + Guid.NewGuid().ToString("N"));

        public SimScenarioStoreTests() => Directory.CreateDirectory(_tempDir);

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }

        private static SimScenarioStore NewStore() =>
            new(new ISimCombatantPolicy[] { new MeleeOnlyPolicy(), new RoundRobinPolicy(), new CooldownFirstPolicy(new AbilityRegistry()) });

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
    }
}
