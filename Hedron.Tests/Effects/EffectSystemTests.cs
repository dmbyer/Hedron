using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Effects
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="EffectSystem"/>.
    /// Tier 4 — persistence round-trip for <see cref="EffectsComponent"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/effect-substrate.md.
    /// </summary>
    public sealed class EffectSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a fresh <see cref="EffectSystem"/> backed by the given <see cref="EntityService"/>.
        /// Optional contributors allow testing the INV-24 GetModifiers seam.
        /// </summary>
        private static EffectSystem Build(EntityService ecs, IEnumerable<IEffectContributor>? contributors = null)
            => new EffectSystem(ecs, contributors ?? Array.Empty<IEffectContributor>());

        /// <summary>
        /// Returns an <see cref="EffectDefinition"/> for a <see cref="EffectKind.StatModifier"/>
        /// with the given policy, duration (negative = UntilRemoved), and power magnitude.
        /// </summary>
        private static EffectDefinition StatModDef(
            string id,
            StackPolicy policy,
            float duration,
            int magnitude,
            ScoreId score = ScoreId.Body,
            EffectPhase phase = EffectPhase.Normal)
            => new EffectDefinition(
                EffectId: id,
                Kind: EffectKind.StatModifier,
                Params: new EffectParams(TargetScore: score, BaseMagnitude: magnitude),
                Category: EffectCategory.Buff,
                PowerScalingFormula: "fixed",
                Duration: duration,
                Stacking: policy,
                Phase: phase);

        /// <summary>
        /// Returns an <see cref="EffectDefinition"/> for a <see cref="EffectKind.Periodic"/>
        /// (HoT / DoT) effect with the given phase and duration.
        /// </summary>
        private static EffectDefinition PeriodicDef(
            string id,
            EffectPhase phase,
            float duration = 30f,
            int magnitude = 5,
            StackPolicy policy = StackPolicy.Stack)
            => new EffectDefinition(
                EffectId: id,
                Kind: EffectKind.Periodic,
                Params: new EffectParams(TargetScore: ScoreId.HpCurrent, BaseMagnitude: magnitude),
                Category: EffectCategory.Buff,
                PowerScalingFormula: "fixed",
                Duration: duration,
                Stacking: policy,
                Phase: phase);

        // ── Apply — HighestWins blocks weaker re-apply ────────────────────────────

        /// <summary>
        /// When an effect with <see cref="StackPolicy.HighestWins"/> is already active and
        /// the existing Power is greater than or equal to the new effect's Power,
        /// <see cref="EffectSystem.Apply"/> must return <c>null</c> (blocked).
        /// </summary>
        [Fact]
        public void Apply_HighestWins_returns_null_when_existing_power_is_greater()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            // Apply a strong empower (power=10).
            var strongDef = StatModDef("empower", StackPolicy.HighestWins, duration: 30f, magnitude: 10);
            var first = system.Apply(target, strongDef, source);
            Assert.NotNull(first);
            Assert.Equal(10, first!.Power);

            // Re-apply with a weaker version (power=5) — must be blocked.
            var weakDef = StatModDef("empower", StackPolicy.HighestWins, duration: 30f, magnitude: 5);
            var blocked = system.Apply(target, weakDef, source);

            Assert.True(blocked is null, "HighestWins must return null when existing power >= new power");

            // The stored effect must still be the original (power=10).
            var active = ecs.Get<EffectsComponent>(target).Effects;
            Assert.Single(active);
            Assert.Equal(10, active[0].Power);
        }

        [Fact]
        public void Apply_HighestWins_returns_null_when_existing_power_is_equal()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            var def = StatModDef("empower", StackPolicy.HighestWins, duration: 30f, magnitude: 8);
            system.Apply(target, def, source); // first apply

            // Same power — must also be blocked (existing.Power >= power).
            var blocked = system.Apply(target, def, source);
            Assert.True(blocked is null, "HighestWins must return null when existing power == new power");
        }

        [Fact]
        public void Apply_HighestWins_replaces_when_new_power_is_greater()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            var weak = StatModDef("empower", StackPolicy.HighestWins, duration: 30f, magnitude: 3);
            system.Apply(target, weak, source);

            var strong = StatModDef("empower", StackPolicy.HighestWins, duration: 30f, magnitude: 15);
            var result = system.Apply(target, strong, source);

            Assert.NotNull(result);
            Assert.Equal(15, result!.Power);

            var active = ecs.Get<EffectsComponent>(target).Effects;
            Assert.Single(active);
            Assert.Equal(15, active[0].Power);
        }

        // ── Apply — Stack policy accumulates ─────────────────────────────────────

        [Fact]
        public void Apply_Stack_policy_accumulates_multiple_effects_with_same_key()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            var def = StatModDef("regen_stack", StackPolicy.Stack, duration: 20f, magnitude: 3);

            system.Apply(target, def, source);
            system.Apply(target, def, source);
            system.Apply(target, def, source);

            var active = ecs.Get<EffectsComponent>(target).Effects;
            Assert.Equal(3, active.Count);
            Assert.All(active, e => Assert.Equal("regen_stack", e.EffectId));
        }

        // ── AdvanceTick — expires timed effects after duration ────────────────────

        [Fact]
        public void AdvanceTick_expires_timed_effect_when_elapsed_meets_duration()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            // Timed effect: duration = 5 seconds.
            var def = StatModDef("short_buff", StackPolicy.Stack, duration: 5f, magnitude: 2);
            system.Apply(target, def, source);

            // Advance exactly to the duration threshold.
            var result = system.AdvanceTick(TimeSpan.FromSeconds(5));

            Assert.Single(result.Expired);
            Assert.Equal(target, result.Expired[0].EntityId);
            Assert.Equal("short_buff", result.Expired[0].Effect.EffectId);
        }

        [Fact]
        public void AdvanceTick_does_not_expire_effect_before_duration()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            var def = StatModDef("medium_buff", StackPolicy.Stack, duration: 10f, magnitude: 2);
            system.Apply(target, def, source);

            // Only 3 of 10 seconds elapsed — should not expire.
            var result = system.AdvanceTick(TimeSpan.FromSeconds(3));

            Assert.Empty(result.Expired);
        }

        // ── AdvanceTick — removes expired effects from the component ──────────────

        [Fact]
        public void AdvanceTick_removes_expired_effect_from_EffectsComponent()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            var def = StatModDef("quick_buff", StackPolicy.Stack, duration: 2f, magnitude: 1);
            system.Apply(target, def, source);

            // Expire it.
            system.AdvanceTick(TimeSpan.FromSeconds(2));

            var comp = ecs.Get<EffectsComponent>(target);
            Assert.Empty(comp.Effects);
        }

        [Fact]
        public void AdvanceTick_keeps_UntilRemoved_effects_indefinitely()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            // Duration = -1 maps to UntilRemoved.
            var def = StatModDef("curse", StackPolicy.Stack, duration: -1f, magnitude: 5);
            system.Apply(target, def, source);

            // Advance by a very large elapsed time — the effect must persist.
            system.AdvanceTick(TimeSpan.FromHours(1));

            var comp = ecs.Get<EffectsComponent>(target);
            Assert.Single(comp.Effects);
            Assert.Equal(EffectLifetime.UntilRemoved, comp.Effects[0].Lifetime);
        }

        // ── AdvanceTick — returns periodic-due effects ────────────────────────────

        [Fact]
        public void AdvanceTick_returns_due_periodic_applications()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            var def = PeriodicDef("hot", EffectPhase.Normal, duration: -1f, magnitude: 4);
            system.Apply(target, def, source);

            var result = system.AdvanceTick(TimeSpan.FromSeconds(2));

            Assert.Single(result.DueApplications);
            Assert.Equal(target, result.DueApplications[0].EntityId);
            Assert.Equal("hot", result.DueApplications[0].Effect.EffectId);
        }

        // ── AdvanceTick — DueApplications sorted Early→Normal→Late ───────────────

        [Fact]
        public void AdvanceTick_DueApplications_sorted_Early_then_Normal_then_Late()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            // Add periodic effects out of order (Late first, then Early, then Normal).
            var lateDef   = PeriodicDef("dot_late",   EffectPhase.Late,   duration: -1f);
            var earlyDef  = PeriodicDef("hot_early",  EffectPhase.Early,  duration: -1f);
            var normalDef = PeriodicDef("hot_normal", EffectPhase.Normal, duration: -1f);

            system.Apply(target, lateDef,   source);
            system.Apply(target, earlyDef,  source);
            system.Apply(target, normalDef, source);

            var result = system.AdvanceTick(TimeSpan.FromSeconds(2));

            Assert.Equal(3, result.DueApplications.Count);
            Assert.Equal(EffectPhase.Early,  result.DueApplications[0].Effect.Phase);
            Assert.Equal(EffectPhase.Normal, result.DueApplications[1].Effect.Phase);
            Assert.Equal(EffectPhase.Late,   result.DueApplications[2].Effect.Phase);
        }

        [Fact]
        public void AdvanceTick_Expired_sorted_Early_then_Normal_then_Late()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            // Three timed buffs (all expire at 5s) with different phases, added out of order.
            var lateDef   = StatModDef("buff_late",   StackPolicy.Stack, duration: 5f, magnitude: 1, phase: EffectPhase.Late);
            var earlyDef  = StatModDef("buff_early",  StackPolicy.Stack, duration: 5f, magnitude: 1, phase: EffectPhase.Early);
            var normalDef = StatModDef("buff_normal", StackPolicy.Stack, duration: 5f, magnitude: 1, phase: EffectPhase.Normal);

            system.Apply(target, lateDef,   source);
            system.Apply(target, earlyDef,  source);
            system.Apply(target, normalDef, source);

            var result = system.AdvanceTick(TimeSpan.FromSeconds(5));

            Assert.Equal(3, result.Expired.Count);
            Assert.Equal(EffectPhase.Early,  result.Expired[0].Effect.Phase);
            Assert.Equal(EffectPhase.Normal, result.Expired[1].Effect.Phase);
            Assert.Equal(EffectPhase.Late,   result.Expired[2].Effect.Phase);
        }

        // ── GetModifiers — sums stored StatModifier effects ──────────────────────

        [Fact]
        public void GetModifiers_sums_StatModifier_effects_targeting_the_same_ScoreId()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            var def1 = StatModDef("buff_a", StackPolicy.Stack, duration: -1f, magnitude: 5,  score: ScoreId.Body);
            var def2 = StatModDef("buff_b", StackPolicy.Stack, duration: -1f, magnitude: 3,  score: ScoreId.Body);
            var def3 = StatModDef("buff_c", StackPolicy.Stack, duration: -1f, magnitude: 10, score: ScoreId.Mind);

            system.Apply(target, def1, source);
            system.Apply(target, def2, source);
            system.Apply(target, def3, source); // different ScoreId — must not be included

            var bodyMod = system.GetModifiers(target, ScoreId.Body);
            Assert.Equal(8, bodyMod); // 5+3

            var mindMod = system.GetModifiers(target, ScoreId.Mind);
            Assert.Equal(10, mindMod);
        }

        [Fact]
        public void GetModifiers_returns_zero_when_no_effects_are_active()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();

            Assert.Equal(0, system.GetModifiers(target, ScoreId.Body));
        }

        // ── GetModifiers — includes IEffectContributor values (INV-24) ────────────

        [Fact]
        public void GetModifiers_includes_contributor_modifiers()
        {
            var ecs = new EntityService();

            // Stub contributor that always returns +7 to Body.
            var contributor = new StubEffectContributor(bodyBonus: 7);
            var system = Build(ecs, new[] { contributor });

            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            // Also apply a stored StatModifier (+5 Body).
            var def = StatModDef("stored_buff", StackPolicy.Stack, duration: -1f, magnitude: 5, score: ScoreId.Body);
            system.Apply(target, def, source);

            var total = system.GetModifiers(target, ScoreId.Body);
            Assert.Equal(12, total); // 5 stored + 7 contributor (INV-24)
        }

        [Fact]
        public void GetModifiers_uses_only_contributor_when_no_stored_effects()
        {
            var ecs = new EntityService();
            var contributor = new StubEffectContributor(bodyBonus: 4);
            var system = Build(ecs, new[] { contributor });
            var target = new EntityBuilder(ecs).AsPlayer().Build();

            Assert.Equal(4, system.GetModifiers(target, ScoreId.Body));
        }

        // ── GetModifiers — only StatModifier kind is summed ───────────────────────

        [Fact]
        public void GetModifiers_ignores_non_StatModifier_effects()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            // Periodic effect — should NOT be counted as a modifier.
            var periodicDef = PeriodicDef("hot", EffectPhase.Normal, duration: -1f, magnitude: 10);
            system.Apply(target, periodicDef, source);

            Assert.Equal(0, system.GetModifiers(target, ScoreId.HpCurrent));
        }

        // ── RemoveByCategory ────────────────────────────────────────────────────

        [Fact]
        public void RemoveByCategory_strips_all_effects_with_matching_category()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();

            // Apply two Curse-category effects and one Buff-category effect.
            var curseDef1 = new EffectDefinition(
                "curse_a", EffectKind.StatModifier, new EffectParams(ScoreId.Body, -3), EffectCategory.Curse,
                "fixed", -1f, StackPolicy.Stack, EffectPhase.Normal);
            var curseDef2 = new EffectDefinition(
                "curse_b", EffectKind.StatModifier, new EffectParams(ScoreId.Mind, -2), EffectCategory.Curse,
                "fixed", -1f, StackPolicy.Stack, EffectPhase.Normal);
            var buffDef = StatModDef("empower", StackPolicy.Stack, duration: -1f, magnitude: 5);

            system.Apply(target, curseDef1, source);
            system.Apply(target, curseDef2, source);
            system.Apply(target, buffDef,   source);

            system.RemoveByCategory(target, EffectCategory.Curse);

            var active = ecs.Get<EffectsComponent>(target).Effects;
            Assert.Single(active);
            Assert.Equal(EffectCategory.Buff, active[0].Category);
        }

        // ── EffectSystem does not hold IEventBus (INV-5) ─────────────────────────

        [Fact]
        public void EffectSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(EffectSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: EffectSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }

        // ── Tier 4: EffectsComponent persistence round-trip ──────────────────────

        /// <summary>
        /// <see cref="EffectsComponent"/> is <c>[Persistent]</c> and filtered by the
        /// <see cref="EffectsComponentJsonConverter"/>: only <see cref="EffectLifetime.UntilRemoved"/>
        /// entries are written to disk.
        /// </summary>
        [Fact]
        public async Task EffectsComponent_round_trip_persists_only_UntilRemoved_effects()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;
            var system = Build(ecs);

            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();
            ecs.AddComponent(target, new PersistentEntity());

            // UntilRemoved: duration = -1.
            var permanentDef = StatModDef("perm_curse", StackPolicy.Stack, duration: -1f, magnitude: 5);
            system.Apply(target, permanentDef, source);

            // Timed: duration = 30s — must NOT survive the round-trip.
            var timedDef = StatModDef("short_buff", StackPolicy.Stack, duration: 30f, magnitude: 3);
            system.Apply(target, timedDef, source);

            await harness.SaveAsync(target);
            var fresh = await harness.ReloadIntoFreshWorld();

            Assert.True(fresh.HasComponent<EffectsComponent>(target),
                "EffectsComponent must survive the round-trip");

            var effects = fresh.Get<EffectsComponent>(target).Effects;

            // Only the UntilRemoved effect should be present.
            Assert.Single(effects);
            Assert.Equal("perm_curse", effects[0].EffectId);
            Assert.Equal(EffectLifetime.UntilRemoved, effects[0].Lifetime);
        }

        [Fact]
        public async Task EffectsComponent_round_trip_WhileKnown_effect_is_not_stored()
        {
            // WhileKnown effects are source-bound / derived — they must never be written
            // to the persistent store and are therefore absent after reload.
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var target = new EntityBuilder(ecs).AsPlayer().Build();
            ecs.AddComponent(target, new PersistentEntity());

            // Manually seed a WhileKnown effect directly into the component
            // (bypassing Apply which would not produce WhileKnown from standard defs).
            var whileKnownEffect = new Effect(
                EffectId: "ability_passive",
                Kind: EffectKind.StatModifier,
                Params: new EffectParams(ScoreId.Body, 2),
                Category: EffectCategory.Buff,
                Power: 2,
                Source: new EffectSource(target),
                Group: null,
                Lifetime: EffectLifetime.WhileKnown,
                Duration: 0f,
                Elapsed: 0f,
                Stacking: StackPolicy.Stack,
                Phase: EffectPhase.Normal);

            var comp = new EffectsComponent();
            comp.Effects.Add(whileKnownEffect);
            ecs.AddComponent(target, comp);

            await harness.SaveAsync(target);
            var fresh = await harness.ReloadIntoFreshWorld();

            // If EffectsComponent round-tripped at all, the WhileKnown entry must be absent.
            if (fresh.HasComponent<EffectsComponent>(target))
            {
                var effects = fresh.Get<EffectsComponent>(target).Effects;
                Assert.DoesNotContain(effects, e => e.Lifetime == EffectLifetime.WhileKnown);
            }
            // (If the component was not stored at all, the invariant is trivially satisfied —
            //  no WhileKnown data reached the database.)
        }

        [Fact]
        public async Task EffectsComponent_round_trip_UntilRemoved_restores_equal()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;
            var system = Build(ecs);

            var target = new EntityBuilder(ecs).AsPlayer().Build();
            var source = new EntityBuilder(ecs).AsPlayer().Build();
            ecs.AddComponent(target, new PersistentEntity());

            // Apply two UntilRemoved effects.
            var def1 = StatModDef("curse_a", StackPolicy.Stack, duration: -1f, magnitude: 4, score: ScoreId.Body);
            var def2 = StatModDef("curse_b", StackPolicy.Stack, duration: -1f, magnitude: 2, score: ScoreId.Mind);
            system.Apply(target, def1, source);
            system.Apply(target, def2, source);

            var before = ecs.Get<EffectsComponent>(target).Effects.ToList();

            await harness.SaveAsync(target);
            var fresh = await harness.ReloadIntoFreshWorld();

            var after = fresh.Get<EffectsComponent>(target).Effects;

            Assert.Equal(before.Count, after.Count);
            for (var i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].EffectId, after[i].EffectId);
                Assert.Equal(before[i].Power,    after[i].Power);
                Assert.Equal(before[i].Lifetime, after[i].Lifetime);
                Assert.Equal(before[i].Kind,     after[i].Kind);
            }
        }
    }

    // ── Test doubles ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Stub <see cref="IEffectContributor"/> that returns a fixed Body bonus and nothing
    /// for any other <see cref="ScoreId"/>.
    /// </summary>
    file sealed class StubEffectContributor : IEffectContributor
    {
        private readonly int _bodyBonus;

        public StubEffectContributor(int bodyBonus) => _bodyBonus = bodyBonus;

        public int GetModifiers(uint entityId, ScoreId scoreId)
            => scoreId == ScoreId.Body ? _bodyBonus : 0;

        public IEnumerable<Effect> GetActive(uint entityId)
            => Enumerable.Empty<Effect>();
    }
}
