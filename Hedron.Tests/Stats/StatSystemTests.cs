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
            public EffectApplyResult Apply(uint targetEntityId, EffectDefinition definition, uint sourceEntityId)
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
            var stats = new StatSystem(attributes, noEffects);
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
            var stats = new StatSystem(attributes, fakeEffects);
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

        // NOTE: weapon DamageBonus rides the WhileEquipped effect contributor
        // (EquipmentEffectContributor). Since combat-effective-scores GetEffectiveAttackPower is a
        // thin wrapper over Get(ScoreId.AttackPower), so it is gear-inclusive. End-to-end
        // equipment→stat coverage lives in EquipmentEffectContributorTests.

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

        // (Equipment-bonus + effect-modifier combination is proven end-to-end in
        // EquipmentEffectContributorTests, where the real contributor folds gear into GetModifiers.)

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

        // ── combat-effective-scores: derived scores from effective Body ─────────

        /// <summary>
        /// Builds a <see cref="StatSystem"/> whose <see cref="AttributeSystem"/> shares the same
        /// <see cref="FakeEffectSystem"/>, so <c>&lt;Pool&gt;Max</c> modifiers reach the pool-cap seam too.
        /// </summary>
        private static (StatSystem stats, AttributeSystem attributes, EntityService ecs, FakeEffectSystem fakeEffects)
            BuildSharedFakeEffects()
        {
            var ecs = new EntityService();
            var fakeEffects = new FakeEffectSystem();
            var attributes = new AttributeSystem(ecs, fakeEffects, Options.Create(new DeathOptions { HpFloor = -10 }));
            return (new StatSystem(attributes, fakeEffects), attributes, ecs, fakeEffects);
        }

        [Fact]
        public void Get_AttackPower_derives_from_effective_Body()
        {
            var (stats, _, ecs, fake) = BuildWithFakeEffects();
            var e = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Build();
            Assert.Equal(5, stats.Get(e, ScoreId.AttackPower)); // no Body modifier: equals the old base

            fake.SetModifier(e, ScoreId.Body, 6);
            Assert.Equal(8, stats.Get(e, ScoreId.AttackPower)); // (10 + 6) / 2

            fake.SetModifier(e, ScoreId.Body, 7);
            fake.SetModifier(e, ScoreId.AttackPower, 3);
            Assert.Equal(11, stats.Get(e, ScoreId.AttackPower)); // 17 / 2 = 8 (integer division) + 3
        }

        [Fact]
        public void Get_Defense_derives_from_effective_Body()
        {
            var (stats, _, ecs, fake) = BuildWithFakeEffects();
            var e = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Build();
            Assert.Equal(2, stats.Get(e, ScoreId.Defense));

            fake.SetModifier(e, ScoreId.Body, 10);
            fake.SetModifier(e, ScoreId.Defense, 2);
            Assert.Equal(7, stats.Get(e, ScoreId.Defense)); // (10 + 10) / 4 + 2
        }

        [Fact]
        public void Derived_scores_floor_at_zero()
        {
            var (stats, _, ecs, fake) = BuildWithFakeEffects();
            var e = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Build();

            fake.SetModifier(e, ScoreId.Body, -40);
            Assert.Equal(0, stats.Get(e, ScoreId.AttackPower));
            Assert.Equal(0, stats.Get(e, ScoreId.Defense));

            fake.SetModifier(e, ScoreId.Body, 0);
            fake.SetModifier(e, ScoreId.AttackPower, -50);
            fake.SetModifier(e, ScoreId.Defense, -50);
            Assert.Equal(0, stats.Get(e, ScoreId.AttackPower));
            Assert.Equal(0, stats.Get(e, ScoreId.Defense));
        }

        [Theory]
        [InlineData(ScoreId.Mind)]
        [InlineData(ScoreId.Body)]
        [InlineData(ScoreId.Spirit)]
        [InlineData(ScoreId.Attunement)]
        [InlineData(ScoreId.AttackPower)]
        [InlineData(ScoreId.Defense)]
        [InlineData(ScoreId.HpCurrent)]
        [InlineData(ScoreId.HpMax)]
        public void Typed_getters_equal_Get(ScoreId score)
        {
            var (stats, _, ecs, fake) = BuildSharedFakeEffects();
            var e = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 12).WithPools(hp: 100).Build();
            foreach (var s in new[] { ScoreId.Mind, ScoreId.Body, ScoreId.Spirit, ScoreId.Attunement, ScoreId.AttackPower, ScoreId.Defense, ScoreId.HpMax })
                fake.SetModifier(e, s, 3);

            int typed = score switch
            {
                ScoreId.Mind => stats.GetEffectiveMind(e),
                ScoreId.Body => stats.GetEffectiveBody(e),
                ScoreId.Spirit => stats.GetEffectiveSpirit(e),
                ScoreId.Attunement => stats.GetEffectiveAttunement(e),
                ScoreId.AttackPower => stats.GetEffectiveAttackPower(e),
                ScoreId.Defense => stats.GetEffectiveDefense(e),
                ScoreId.HpCurrent => stats.GetCurrentHp(e),
                _ => stats.GetMaxHp(e),
            };
            Assert.Equal(stats.Get(e, score), typed);
        }

        [Theory]
        [InlineData(ResourceType.Hp, ScoreId.HpMax, ScoreId.HpCurrent)]
        [InlineData(ResourceType.Mana, ScoreId.ManaMax, ScoreId.ManaCurrent)]
        [InlineData(ResourceType.Stamina, ScoreId.StaminaMax, ScoreId.StaminaCurrent)]
        [InlineData(ResourceType.Astra, ScoreId.AstraMax, ScoreId.AstraCurrent)]
        public void Pool_reads_delegate_to_attribute_cap(ResourceType pool, ScoreId max, ScoreId current)
        {
            var (stats, attributes, ecs, fake) = BuildSharedFakeEffects();
            var e = new EntityBuilder(ecs).AsPlayer().WithAttributes().WithPools(hp: 100, mana: 50, stamina: 50, astra: 10).Build();

            fake.SetModifier(e, max, 20);
            attributes.SetCurrent(e, pool, 1000); // fills to the raised cap
            Assert.Equal(attributes.GetEffectiveMax(e, pool), stats.Get(e, max));
            Assert.Equal(attributes.GetEffectiveMax(e, pool), stats.Get(e, current));

            // The modifier lapses: stored stays above the cap until reconciled; the read is clamped.
            fake.SetModifier(e, max, 0);
            Assert.Equal(attributes.GetCurrent(e, pool), stats.Get(e, current));
            Assert.Equal(attributes.GetEffectiveMax(e, pool), stats.Get(e, current));

            // Floor-at-1 shared by display and writes.
            fake.SetModifier(e, max, -10_000);
            Assert.Equal(1, stats.Get(e, max));
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
