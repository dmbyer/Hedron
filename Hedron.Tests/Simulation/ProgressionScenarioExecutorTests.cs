using System.Linq;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>
    /// Tier 1 — <see cref="ProgressionScenarioExecutor"/> single-run kill-event loop
    /// (Postconditions 4, 8; Design notes "analytical kill-events over the real IProgressionSystem").
    /// </summary>
    public sealed class ProgressionScenarioExecutorTests
    {
        private static SandboxWorld NewWorld(IRandom random)
        {
            var factory = new SandboxWorldFactory(
                new AbilityRegistry(), new EffectRegistry(),
                new PowerBudgetSystem(PowerBudgetTunables.Default), Options.Create(new DeathOptions()));
            return factory.Create(random);
        }

        private static uint NewCombatant(SandboxWorld world, int body, int mind = 10, int spirit = 10, int attunement = 10, int hp = 1000)
        {
            var entity = world.EntityService.CreateEntity();
            world.EntityService.AddComponent(entity.Id, new MobDataComponent { Name = "probe" });
            world.EntityService.AddComponent(entity.Id, new AttributesComponent { Body = body, Mind = mind, Spirit = spirit, Attunement = attunement, Level = 1 });
            world.EntityService.AddComponent(entity.Id, new PoolsComponent { MaxHp = hp, CurrentHp = hp });
            return entity.Id;
        }

        [Fact]
        public void ExecuteRun_KillsToFirstImprovement_MatchesHandComputedThresholdMath()
        {
            // Equal power on both sides -> anti-grind scale = 1.0 -> Body award = the raw roll,
            // 10 every kill. ThresholdBase 100 / 10 per kill = 10 kills to the first crossing.
            var random = new FakeRandom(Enumerable.Repeat(10, 40).ToArray());
            var world = NewWorld(random);
            var subject = NewCombatant(world, body: 20);
            var victim = NewCombatant(world, body: 20);
            var settings = new ProgressionSettings(ScoreId.Body, TargetImprovements: 1, MaxKillsPerRun: 20);

            var record = new ProgressionScenarioExecutor().ExecuteRun(world, subject, victim, settings, runIndex: 0);

            Assert.True(record.ReachedTarget);
            Assert.Equal(10, record.Kills);
            Assert.Equal(new[] { 10 }, record.MilestoneKills);
        }

        [Fact]
        public void ExecuteRun_MultipleMilestones_RecordsKillCountPerCrossing()
        {
            // Threshold(0) = 100, Threshold(1) = 150. +10/kill: crosses at kill 10 (xp=100) and
            // kill 15 (xp=150).
            var random = new FakeRandom(Enumerable.Repeat(10, 40).ToArray());
            var world = NewWorld(random);
            var subject = NewCombatant(world, body: 20);
            var victim = NewCombatant(world, body: 20);
            var settings = new ProgressionSettings(ScoreId.Body, TargetImprovements: 2, MaxKillsPerRun: 30);

            var record = new ProgressionScenarioExecutor().ExecuteRun(world, subject, victim, settings, runIndex: 0);

            Assert.True(record.ReachedTarget);
            Assert.Equal(15, record.Kills);
            Assert.Equal(new[] { 10, 15 }, record.MilestoneKills);
        }

        [Fact]
        public void ExecuteRun_CapReachedBeforeTarget_YieldsNotReachedRecordWithNoMilestones()
        {
            // +8/kill (minimum roll), 5 kills = 40 xp -- nowhere near the 100 threshold; the run
            // must stop at the cap rather than loop forever.
            var random = new FakeRandom(Enumerable.Repeat(8, 20).ToArray());
            var world = NewWorld(random);
            var subject = NewCombatant(world, body: 20);
            var victim = NewCombatant(world, body: 20);
            var settings = new ProgressionSettings(ScoreId.Body, TargetImprovements: 1, MaxKillsPerRun: 5);

            var record = new ProgressionScenarioExecutor().ExecuteRun(world, subject, victim, settings, runIndex: 0);

            Assert.False(record.ReachedTarget);
            Assert.Equal(5, record.Kills);
            Assert.Empty(record.MilestoneKills);
        }

        [Fact]
        public void ExecuteRun_VictimBelowAntiGrindFloor_YieldsZeroAwardsAndCapReached()
        {
            // Victim power << killer power (ratio well under AntiGrindFloorRatio 0.25) -> every
            // award rounds to zero -> the run exhausts the cap with no improvement whatsoever.
            // scale <= 0 short-circuits the random draw, so an empty FakeRandom is safe here.
            var random = new FakeRandom();
            var world = NewWorld(random);
            var subject = NewCombatant(world, body: 100, mind: 100, spirit: 100, attunement: 100);
            var victim = NewCombatant(world, body: 1, mind: 1, spirit: 1, attunement: 1);
            var settings = new ProgressionSettings(ScoreId.Body, TargetImprovements: 1, MaxKillsPerRun: 10);

            var record = new ProgressionScenarioExecutor().ExecuteRun(world, subject, victim, settings, runIndex: 0);

            Assert.False(record.ReachedTarget);
            Assert.Equal(10, record.Kills);
            Assert.Empty(record.MilestoneKills);
            Assert.Equal(0, record.FinalXp[ScoreId.Body]);
            Assert.Equal(0, record.FinalXp[ScoreId.HpMax]);
        }

        [Fact]
        public void ExecuteRun_FinalXpAndImprovements_EqualWorldProgressionReads()
        {
            // XP mutation happens only via world.Progression -- the record's final snapshot must
            // agree with a direct read through the same seam (no bypass write anywhere in the loop).
            var random = new FakeRandom(Enumerable.Repeat(10, 40).ToArray());
            var world = NewWorld(random);
            var subject = NewCombatant(world, body: 20);
            var victim = NewCombatant(world, body: 20);
            var settings = new ProgressionSettings(ScoreId.Body, TargetImprovements: 1, MaxKillsPerRun: 20);

            var record = new ProgressionScenarioExecutor().ExecuteRun(world, subject, victim, settings, runIndex: 0);

            foreach (var track in ProgressionConstants.CombatTracks)
            {
                Assert.Equal(world.Progression.GetXp(subject, track), record.FinalXp[track]);
                Assert.Equal(world.Progression.GetImprovementCount(subject, track), record.FinalImprovements[track]);
            }
        }

        [Fact]
        public void ExecuteRun_RunIndex_IsCarriedIntoRecord()
        {
            var random = new FakeRandom(Enumerable.Repeat(10, 40).ToArray());
            var world = NewWorld(random);
            var subject = NewCombatant(world, body: 20);
            var victim = NewCombatant(world, body: 20);
            var settings = new ProgressionSettings(ScoreId.Body, TargetImprovements: 1, MaxKillsPerRun: 20);

            var record = new ProgressionScenarioExecutor().ExecuteRun(world, subject, victim, settings, runIndex: 7);

            Assert.Equal(7, record.RunIndex);
        }
    }
}
