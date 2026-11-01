using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>Tier 3 — <see cref="CombatScenarioExecutor"/> single-run flow (Postconditions 3, 7).</summary>
    public sealed class CombatScenarioExecutorTests
    {
        private static SandboxWorld NewWorld(IRandom random)
        {
            var factory = new SandboxWorldFactory(
                new AbilityRegistry(), new EffectRegistry(),
                new PowerBudgetSystem(PowerBudgetTunables.Default), Options.Create(new DeathOptions()));
            return factory.Create(random);
        }

        private static uint NewMobCombatant(SandboxWorld world, int body, int hp)
        {
            var entity = world.EntityService.CreateEntity();
            world.EntityService.AddComponent(entity.Id, new MobDataComponent { Name = "probe" });
            world.EntityService.AddComponent(entity.Id, new AttributesComponent { Body = body, Mind = 10, Spirit = 10, Attunement = 10, Level = 1 });
            world.EntityService.AddComponent(entity.Id, new PoolsComponent { MaxHp = hp, CurrentHp = hp });
            return entity.Id;
        }

        [Fact]
        public void ExecuteRun_LopsidedMatchup_StrongerSideWinsOnFirstTick()
        {
            var world = NewWorld(new FakeRandom(12345));
            var strong = NewMobCombatant(world, body: 30, hp: 200); // attackPower 15, threshold vs weak = 11 — always hits, always kills
            var weak = NewMobCombatant(world, body: 5, hp: 1);

            var executor = new CombatScenarioExecutor();
            var record = executor.ExecuteRun(world, strong, weak, new MeleeOnlyPolicy(), new MeleeOnlyPolicy(), maxTicksPerRun: 20, runIndex: 0);

            Assert.Equal(0, record.WinnerSide);
            Assert.Equal(1, record.Ticks);
            Assert.True(record.SideADamageDealt >= 1);
        }

        [Fact]
        public void ExecuteRun_DamageTotals_ConsistentWithHpLost()
        {
            var world = NewWorld(new FakeRandom(777));
            var strong = NewMobCombatant(world, body: 30, hp: 200);
            var weakHp = 50;
            var weak = NewMobCombatant(world, body: 5, hp: weakHp);

            var executor = new CombatScenarioExecutor();
            var record = executor.ExecuteRun(world, strong, weak, new MeleeOnlyPolicy(), new MeleeOnlyPolicy(), maxTicksPerRun: 30, runIndex: 0);

            var weakHpAfter = world.Stats.GetCurrentHp(weak);
            var strongHpAfter = world.Stats.GetCurrentHp(strong);

            // Damage dealt by the strong side must account for at least the HP the weak side lost
            // (HP can go slightly negative on the killing blow — DamageDealt is the raw hit, not clamped).
            Assert.True(record.SideADamageDealt >= weakHp - weakHpAfter);
            Assert.True(record.SideBDamageDealt >= 200 - strongHpAfter);
        }

        [Fact]
        public void ExecuteRun_MaxTicksReached_YieldsDrawRecord()
        {
            // Both sides effectively immortal relative to the tick cap — neither can die in 3 ticks.
            var world = NewWorld(new FakeRandom(55));
            var a = NewMobCombatant(world, body: 10, hp: 1_000_000);
            var b = NewMobCombatant(world, body: 10, hp: 1_000_000);

            var executor = new CombatScenarioExecutor();
            var record = executor.ExecuteRun(world, a, b, new MeleeOnlyPolicy(), new MeleeOnlyPolicy(), maxTicksPerRun: 3, runIndex: 0);

            Assert.Null(record.WinnerSide);
            Assert.Equal(3, record.Ticks);
        }

        [Fact]
        public void ExecuteRun_RunIndex_IsCarriedIntoRecord()
        {
            var world = NewWorld(new FakeRandom(1));
            var a = NewMobCombatant(world, body: 30, hp: 200);
            var b = NewMobCombatant(world, body: 5, hp: 1);

            var executor = new CombatScenarioExecutor();
            var record = executor.ExecuteRun(world, a, b, new MeleeOnlyPolicy(), new MeleeOnlyPolicy(), maxTicksPerRun: 5, runIndex: 42);

            Assert.Equal(42, record.RunIndex);
        }

        // ── combat-effective-scores T21 — sandbox reconcile parity ──────────────

        [Fact]
        public void Reconcile_runs_each_tick_and_preserves_determinism()
        {
            RunRecord Run(out int storedHp, out int cap)
            {
                var world = NewWorld(new FakeRandom(2026));
                var a = NewMobCombatant(world, body: 10, hp: 1_000_000);
                var b = NewMobCombatant(world, body: 10, hp: 1_000_000);
                // A 2-second +500 HpMax buff, filled to the raised cap; it expires mid-run.
                Hedron.Tests.Harness.TestEffects.ApplyStatModifier(world.Effects, a, Hedron.Core.Modules.Stats.ScoreId.HpMax, 500, durationSeconds: 2f);
                world.Attributes.RestoreToFraction(a, ResourceType.Hp, 1.0);

                var record = new CombatScenarioExecutor().ExecuteRun(
                    world, a, b, new MeleeOnlyPolicy(), new MeleeOnlyPolicy(), maxTicksPerRun: 5, runIndex: 0);
                storedHp = world.EntityService.Get<PoolsComponent>(a).CurrentHp;
                cap = world.Attributes.GetEffectiveMax(a, ResourceType.Hp);
                return record;
            }

            var first = Run(out var storedHp, out var cap);
            var second = Run(out _, out _);

            Assert.Equal(1_000_000, cap);       // the buff lapsed
            Assert.True(storedHp <= cap);       // and the executor wrote the clamp back
            Assert.Equal(first, second);        // reconcile draws nothing: same seed, same record
        }
    }
}
