using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Stats
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="StatSystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/stat-system.md.
    /// </summary>
    public sealed class StatSystemTests
    {
        // ── FakeEffectSystem ─────────────────────────────────────────────────────

        /// <summary>
        /// Minimal hand-rolled fake for <see cref="IEffectSystem"/> that lets tests inject
        /// per-entity, per-score modifiers without requiring a real <see cref="EffectSystem"/>
        /// or live effect state.
        /// </summary>
        private sealed class FakeEffectSystem : IEffectSystem
        {
            // (entityId, scoreId) → bonus
            private readonly Dictionary<(uint, ScoreId), int> _modifiers = new();

            /// <summary>Registers a fixed modifier that <see cref="GetModifiers"/> will return.</summary>
            public void SetModifier(uint entityId, ScoreId scoreId, int value)
                => _modifiers[(entityId, scoreId)] = value;

            public int GetModifiers(uint entityId, ScoreId scoreId)
                => _modifiers.TryGetValue((entityId, scoreId), out var v) ? v : 0;

            // ── Unused IEffectSystem members — not exercised by StatSystem ───────
            public Effect? Apply(uint targetEntityId, EffectDefinition definition, uint sourceEntityId)
                => throw new NotSupportedException("FakeEffectSystem.Apply not used by StatSystem tests.");

            public void Remove(uint entityId, string effectId) { }

            public void RemoveByCategory(uint entityId, EffectCategory category) { }

            public void RemoveImpermanent(uint entityId) { }

            public IReadOnlyList<Effect> GetActive(uint entityId)
                => Array.Empty<Effect>();

            public EffectTickResult AdvanceTick(TimeSpan elapsed)
                => new EffectTickResult(
                    Array.Empty<PeriodicApplication>(),
                    Array.Empty<(uint, Effect)>());
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="StatSystem"/> backed by a real <see cref="AttributeSystem"/>
        /// and a real <see cref="EffectSystem"/> (no active effects, no contributors).
        /// Use for tests that do not need to control modifier injection.
        /// </summary>
        private static (StatSystem stats, AttributeSystem attributes, EntityService ecs) BuildReal()
        {
            var ecs = new EntityService();
            var noEffects = new EffectSystem(ecs, Array.Empty<IEffectContributor>());
            var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
            var attributes = new AttributeSystem(ecs, noEffects, deathOpts);
            var stats = new StatSystem(attributes, ecs, noEffects);
            return (stats, attributes, ecs);
        }

        /// <summary>
        /// Builds a <see cref="StatSystem"/> that injects a <see cref="FakeEffectSystem"/>
        /// so callers can control what <c>GetModifiers</c> returns.
        /// The real <see cref="AttributeSystem"/> is wired with a separate no-op
        /// <see cref="EffectSystem"/> so HP-pool clamping behaves normally.
        /// </summary>
        private static (StatSystem stats, AttributeSystem attributes, EntityService ecs, FakeEffectSystem fakeEffects)
            BuildWithFakeEffects()
        {
            var ecs = new EntityService();
            var noEffects = new EffectSystem(ecs, Array.Empty<IEffectContributor>());
            var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
            var attributes = new AttributeSystem(ecs, noEffects, deathOpts);
            var fakeEffects = new FakeEffectSystem();
            var stats = new StatSystem(attributes, ecs, fakeEffects);
            return (stats, attributes, ecs, fakeEffects);
        }

        // ── GetEffectiveAttackPower — base: Body/2 ───────────────────────────────

        /// <summary>
        /// Without any weapon equipped, attack power must equal Body/2 (integer division).
        /// </summary>
        [Fact]
        public void GetEffectiveAttackPower_equals_Body_over_2_with_no_weapon()
        {
            var (stats, _, ecs) = BuildReal();
            var attacker = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 14).Build();

            Assert.Equal(7, stats.GetEffectiveAttackPower(attacker)); // 14 / 2 = 7
        }

        [Fact]
        public void GetEffectiveAttackPower_odd_body_uses_integer_division()
        {
            var (stats, _, ecs) = BuildReal();
            var attacker = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 11).Build();

            Assert.Equal(5, stats.GetEffectiveAttackPower(attacker)); // 11 / 2 = 5
        }

        [Fact]
        public void GetEffectiveAttackPower_returns_zero_base_when_body_is_zero()
        {
            var (stats, _, ecs) = BuildReal();
            var attacker = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 0).Build();

            Assert.Equal(0, stats.GetEffectiveAttackPower(attacker));
        }

        // ── GetEffectiveAttackPower — adds MainHand DamageBonus ─────────────────

        /// <summary>
        /// Equipping a weapon with a <c>DamageBonus</c> adds that bonus on top of Body/2.
        /// </summary>
        [Fact]
        public void GetEffectiveAttackPower_adds_MainHand_DamageBonus()
        {
            var (stats, _, ecs) = BuildReal();

            // Create a weapon item entity with DamageBonus = 5.
            var weapon = ecs.CreateEntity();
            ecs.AddComponent(weapon.Id, new ItemDataComponent { DamageBonus = 5 });

            // Player with Body=10, wielding the weapon.
            var attacker = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .Wielding(weapon.Id)
                .Build();

            // Expected: 10/2 + 5 = 10.
            Assert.Equal(10, stats.GetEffectiveAttackPower(attacker));
        }

        [Fact]
        public void GetEffectiveAttackPower_with_zero_DamageBonus_weapon_equals_base()
        {
            var (stats, _, ecs) = BuildReal();

            var weapon = ecs.CreateEntity();
            ecs.AddComponent(weapon.Id, new ItemDataComponent { DamageBonus = 0 });

            var attacker = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .Wielding(weapon.Id)
                .Build();

            // 10/2 + 0 = 5; same as unarmed.
            Assert.Equal(5, stats.GetEffectiveAttackPower(attacker));
        }

        [Fact]
        public void GetEffectiveAttackPower_unarmed_equals_attack_with_zero_bonus_weapon()
        {
            var (stats, _, ecs) = BuildReal();

            // Build two attackers with the same Body; one unarmed, one with a 0-bonus weapon.
            var unarmed = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Build();

            var weapon = ecs.CreateEntity();
            ecs.AddComponent(weapon.Id, new ItemDataComponent { DamageBonus = 0 });
            var armed = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Wielding(weapon.Id).Build();

            Assert.Equal(stats.GetEffectiveAttackPower(unarmed), stats.GetEffectiveAttackPower(armed));
        }

        [Fact]
        public void GetEffectiveAttackPower_large_DamageBonus_is_added_correctly()
        {
            var (stats, _, ecs) = BuildReal();

            var weapon = ecs.CreateEntity();
            ecs.AddComponent(weapon.Id, new ItemDataComponent { DamageBonus = 20 });

            var attacker = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 8)
                .Wielding(weapon.Id)
                .Build();

            // 8/2 + 20 = 24.
            Assert.Equal(24, stats.GetEffectiveAttackPower(attacker));
        }

        // ── GetEffectiveDefense — Body/4 ─────────────────────────────────────────

        [Fact]
        public void GetEffectiveDefense_equals_Body_over_4()
        {
            var (stats, _, ecs) = BuildReal();
            var defender = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 20).Build();

            Assert.Equal(5, stats.GetEffectiveDefense(defender)); // 20 / 4 = 5
        }

        [Fact]
        public void GetEffectiveDefense_uses_integer_division()
        {
            var (stats, _, ecs) = BuildReal();
            var defender = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 11).Build();

            Assert.Equal(2, stats.GetEffectiveDefense(defender)); // 11 / 4 = 2
        }

        [Fact]
        public void GetEffectiveDefense_returns_zero_when_body_is_zero()
        {
            var (stats, _, ecs) = BuildReal();
            var defender = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 0).Build();

            Assert.Equal(0, stats.GetEffectiveDefense(defender));
        }

        // ── Get(ScoreId) — folds IEffectSystem.GetModifiers ─────────────────────

        /// <summary>
        /// <see cref="StatSystem.Get"/> for a stat score must include the value from
        /// <c>IEffectSystem.GetModifiers</c>. Here we inject a <see cref="FakeEffectSystem"/>
        /// that returns a fixed bonus for <see cref="ScoreId.Body"/>.
        /// </summary>
        [Fact]
        public void Get_Body_includes_effect_modifier()
        {
            var (stats, _, ecs, fakeEffects) = BuildWithFakeEffects();
            var player = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Build();

            // Inject a +4 Body modifier via the fake.
            fakeEffects.SetModifier(player, ScoreId.Body, 4);

            // Get(Body) must return base Body (10) + modifier (4) = 14.
            Assert.Equal(14, stats.Get(player, ScoreId.Body));
        }

        [Fact]
        public void Get_Mind_includes_effect_modifier()
        {
            var (stats, _, ecs, fakeEffects) = BuildWithFakeEffects();
            var player = new EntityBuilder(ecs).AsPlayer().WithAttributes(mind: 12).Build();

            fakeEffects.SetModifier(player, ScoreId.Mind, 3);

            Assert.Equal(15, stats.Get(player, ScoreId.Mind)); // 12 + 3
        }

        [Fact]
        public void Get_AttackPower_includes_effect_modifier()
        {
            var (stats, _, ecs, fakeEffects) = BuildWithFakeEffects();
            var player = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Build();

            // AttackPower base = Body/2 = 5; inject +6 modifier.
            fakeEffects.SetModifier(player, ScoreId.AttackPower, 6);

            Assert.Equal(11, stats.Get(player, ScoreId.AttackPower)); // 5 + 6
        }

        [Fact]
        public void Get_Defense_includes_effect_modifier()
        {
            var (stats, _, ecs, fakeEffects) = BuildWithFakeEffects();
            var player = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 12).Build();

            // Defense base = Body/4 = 3; inject +2 modifier.
            fakeEffects.SetModifier(player, ScoreId.Defense, 2);

            Assert.Equal(5, stats.Get(player, ScoreId.Defense)); // 3 + 2
        }

        [Fact]
        public void Get_returns_base_value_when_no_modifiers()
        {
            var (stats, _, ecs, _) = BuildWithFakeEffects();
            var player = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10, mind: 12).Build();

            Assert.Equal(10, stats.Get(player, ScoreId.Body));
            Assert.Equal(12, stats.Get(player, ScoreId.Mind));
        }

        // ── Equipment + effect modifiers both included ───────────────────────────

        /// <summary>
        /// Attack power must include BOTH the MainHand DamageBonus from equipment AND
        /// any active <see cref="ScoreId.AttackPower"/> modifier from the effect system.
        /// </summary>
        [Fact]
        public void Get_AttackPower_includes_both_equipment_bonus_and_effect_modifier()
        {
            var (stats, _, ecs, fakeEffects) = BuildWithFakeEffects();

            // Weapon with DamageBonus = 4.
            var weapon = ecs.CreateEntity();
            ecs.AddComponent(weapon.Id, new ItemDataComponent { DamageBonus = 4 });

            // Player: Body=10, wielding weapon, +3 AttackPower from active effect.
            var player = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .Wielding(weapon.Id)
                .Build();

            fakeEffects.SetModifier(player, ScoreId.AttackPower, 3);

            // Expected: Body/2 (5) + DamageBonus (4) + effect modifier (3) = 12.
            Assert.Equal(12, stats.Get(player, ScoreId.AttackPower));
        }

        // ── Active StatModifier effect increases targeted stat score ─────────────

        /// <summary>
        /// An active <see cref="ScoreId.Body"/> modifier from the effect system must
        /// increase the value returned by <see cref="StatSystem.Get"/> for Body.
        /// </summary>
        [Fact]
        public void Active_StatModifier_effect_increases_targeted_stat_score()
        {
            var (stats, _, ecs, fakeEffects) = BuildWithFakeEffects();
            var player = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Build();

            var before = stats.Get(player, ScoreId.Body);
            Assert.Equal(10, before);

            // Simulate an active +5 Body modifier (e.g. from "strength" spell).
            fakeEffects.SetModifier(player, ScoreId.Body, 5);

            var after = stats.Get(player, ScoreId.Body);
            Assert.Equal(15, after);
        }

        // ── StatSystem does not hold IEventBus (INV-5) ──────────────────────────

        [Fact]
        public void StatSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(StatSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: StatSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }
    }
}
