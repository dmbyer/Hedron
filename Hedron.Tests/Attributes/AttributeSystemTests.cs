using System;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Attributes
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="AttributeSystem"/>.
    /// Coverage contract: clamp invariants documented in <see cref="IAttributeSystem"/>.
    ///   • SetCurrentHp clamps to [HpFloor, effectiveMaxHp]; floor default = -10.
    ///   • SetCurrentMana / SetCurrentStamina / SetCurrentAstra clamp to [0, effectiveMax].
    ///   • SetMaxX downgrades CurrentX if it would exceed the new max.
    ///   • Getters return stored values unchanged.
    ///   • Effect modifiers (via IEffectSystem.GetModifiers) are respected by current-pool setters.
    ///   • Missing component: getters return defaults; setters are no-ops.
    /// INV-5: AttributeSystem never touches the event bus (structural guard included).
    /// </summary>
    public sealed class AttributeSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a fully wired <see cref="AttributeSystem"/> with no active effects
        /// and the default HpFloor (-10).
        /// </summary>
        private static (AttributeSystem system, EntityService ecs) Build(int hpFloor = -10)
        {
            var ecs = new EntityService();
            var noEffects = new EffectSystem(ecs, Array.Empty<IEffectContributor>());
            var deathOpts = Options.Create(new DeathOptions { HpFloor = hpFloor });
            return (new AttributeSystem(ecs, noEffects, deathOpts), ecs);
        }

        // ── GetCurrentHp / GetMaxHp — return stored values ────────────────────

        [Fact]
        public void GetCurrentHp_returns_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(hp: 80).Build();

            Assert.Equal(80, sys.GetCurrentHp(id));
        }

        [Fact]
        public void GetMaxHp_returns_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(hp: 120).Build();

            Assert.Equal(120, sys.GetMaxHp(id));
        }

        [Fact]
        public void GetCurrentMana_returns_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(mana: 40).Build();

            Assert.Equal(40, sys.GetCurrentMana(id));
        }

        [Fact]
        public void GetMaxMana_returns_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(mana: 60).Build();

            Assert.Equal(60, sys.GetMaxMana(id));
        }

        // ── SetCurrentHp — clamp invariants ──────────────────────────────────

        [Fact]
        public void SetCurrentHp_stores_value_within_range()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(hp: 100).Build();

            sys.SetCurrentHp(id, 50);

            Assert.Equal(50, sys.GetCurrentHp(id));
        }

        [Fact]
        public void SetCurrentHp_clamps_to_max_when_value_exceeds_max()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(hp: 100).Build();

            sys.SetCurrentHp(id, 999);

            Assert.Equal(100, sys.GetCurrentHp(id));
        }

        [Fact]
        public void SetCurrentHp_clamps_to_HpFloor_when_value_below_floor()
        {
            var (sys, ecs) = Build(hpFloor: -10);
            var id = new EntityBuilder(ecs).WithPools(hp: 100).Build();

            sys.SetCurrentHp(id, -999);

            Assert.Equal(-10, sys.GetCurrentHp(id));
        }

        [Fact]
        public void SetCurrentHp_allows_exact_HpFloor_value()
        {
            var (sys, ecs) = Build(hpFloor: -10);
            var id = new EntityBuilder(ecs).WithPools(hp: 100).Build();

            sys.SetCurrentHp(id, -10);

            Assert.Equal(-10, sys.GetCurrentHp(id));
        }

        [Fact]
        public void SetCurrentHp_allows_exact_max_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(hp: 100).Build();

            sys.SetCurrentHp(id, 100);

            Assert.Equal(100, sys.GetCurrentHp(id));
        }

        [Fact]
        public void SetCurrentHp_negative_values_above_floor_are_stored()
        {
            var (sys, ecs) = Build(hpFloor: -10);
            var id = new EntityBuilder(ecs).WithPools(hp: 100).Build();

            sys.SetCurrentHp(id, -5);

            Assert.Equal(-5, sys.GetCurrentHp(id));
        }

        [Fact]
        public void SetCurrentHp_HpFloor_customizable_via_DeathOptions()
        {
            // A custom floor of 0 means HP cannot go negative at all.
            var (sys, ecs) = Build(hpFloor: 0);
            var id = new EntityBuilder(ecs).WithPools(hp: 100).Build();

            sys.SetCurrentHp(id, -1);

            Assert.Equal(0, sys.GetCurrentHp(id));
        }

        // ── SetMaxHp — clamps current when new max is lower ──────────────────

        [Fact]
        public void SetMaxHp_updates_max()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(hp: 100).Build();

            sys.SetMaxHp(id, 150);

            Assert.Equal(150, sys.GetMaxHp(id));
        }

        [Fact]
        public void SetMaxHp_does_not_change_current_when_current_is_below_new_max()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(hp: 100).Build();
            sys.SetCurrentHp(id, 60);

            sys.SetMaxHp(id, 150);

            Assert.Equal(60, sys.GetCurrentHp(id));
        }

        [Fact]
        public void SetMaxHp_clamps_current_down_when_new_max_is_lower_than_current()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(hp: 100).Build();
            // CurrentHp == 100; lower max to 50 → current must drop to 50.

            sys.SetMaxHp(id, 50);

            Assert.Equal(50, sys.GetCurrentHp(id));
            Assert.Equal(50, sys.GetMaxHp(id));
        }

        [Fact]
        public void SetMaxHp_equal_to_current_leaves_current_unchanged()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(hp: 100).Build();

            sys.SetMaxHp(id, 100);

            Assert.Equal(100, sys.GetCurrentHp(id));
        }

        // ── SetCurrentMana — clamp to [0, max] ───────────────────────────────

        [Fact]
        public void SetCurrentMana_stores_value_within_range()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(mana: 50).Build();

            sys.SetCurrentMana(id, 30);

            Assert.Equal(30, sys.GetCurrentMana(id));
        }

        [Fact]
        public void SetCurrentMana_clamps_to_max_when_value_exceeds_max()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(mana: 50).Build();

            sys.SetCurrentMana(id, 999);

            Assert.Equal(50, sys.GetCurrentMana(id));
        }

        [Fact]
        public void SetCurrentMana_clamps_to_zero_when_value_is_negative()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(mana: 50).Build();

            sys.SetCurrentMana(id, -1);

            Assert.Equal(0, sys.GetCurrentMana(id));
        }

        [Fact]
        public void SetCurrentMana_allows_exact_zero()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(mana: 50).Build();

            sys.SetCurrentMana(id, 0);

            Assert.Equal(0, sys.GetCurrentMana(id));
        }

        // ── SetMaxMana — clamps current when new max is lower ────────────────

        [Fact]
        public void SetMaxMana_clamps_current_down_when_new_max_is_lower_than_current()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(mana: 50).Build();
            // CurrentMana == 50; lower max to 20 → current must drop to 20.

            sys.SetMaxMana(id, 20);

            Assert.Equal(20, sys.GetCurrentMana(id));
            Assert.Equal(20, sys.GetMaxMana(id));
        }

        [Fact]
        public void SetMaxMana_does_not_change_current_when_current_is_below_new_max()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(mana: 50).Build();
            sys.SetCurrentMana(id, 10);

            sys.SetMaxMana(id, 80);

            Assert.Equal(10, sys.GetCurrentMana(id));
        }

        // ── SetCurrentStamina — clamp to [0, max] ────────────────────────────

        [Fact]
        public void SetCurrentStamina_stores_value_within_range()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(stamina: 50).Build();

            sys.SetCurrentStamina(id, 25);

            Assert.Equal(25, sys.GetCurrentStamina(id));
        }

        [Fact]
        public void SetCurrentStamina_clamps_to_max_when_value_exceeds_max()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(stamina: 50).Build();

            sys.SetCurrentStamina(id, 999);

            Assert.Equal(50, sys.GetCurrentStamina(id));
        }

        [Fact]
        public void SetCurrentStamina_clamps_to_zero_when_value_is_negative()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(stamina: 50).Build();

            sys.SetCurrentStamina(id, -5);

            Assert.Equal(0, sys.GetCurrentStamina(id));
        }

        // ── SetMaxStamina — clamps current when new max is lower ──────────────

        [Fact]
        public void SetMaxStamina_clamps_current_down_when_new_max_is_lower_than_current()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(stamina: 50).Build();

            sys.SetMaxStamina(id, 20);

            Assert.Equal(20, sys.GetCurrentStamina(id));
            Assert.Equal(20, sys.GetMaxStamina(id));
        }

        // ── SetCurrentAstra — clamp to [0, max] ──────────────────────────────

        [Fact]
        public void SetCurrentAstra_stores_value_within_range()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(astra: 10).Build();

            sys.SetCurrentAstra(id, 7);

            Assert.Equal(7, sys.GetCurrentAstra(id));
        }

        [Fact]
        public void SetCurrentAstra_clamps_to_max_when_value_exceeds_max()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(astra: 10).Build();

            sys.SetCurrentAstra(id, 999);

            Assert.Equal(10, sys.GetCurrentAstra(id));
        }

        [Fact]
        public void SetCurrentAstra_clamps_to_zero_when_value_is_negative()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(astra: 10).Build();

            sys.SetCurrentAstra(id, -1);

            Assert.Equal(0, sys.GetCurrentAstra(id));
        }

        // ── SetMaxAstra — clamps current when new max is lower ───────────────

        [Fact]
        public void SetMaxAstra_clamps_current_down_when_new_max_is_lower_than_current()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithPools(astra: 10).Build();

            sys.SetMaxAstra(id, 5);

            Assert.Equal(5, sys.GetCurrentAstra(id));
            Assert.Equal(5, sys.GetMaxAstra(id));
        }

        // ── Attributes (Level, Mind, Body, Spirit, Attunement) ───────────────

        [Fact]
        public void GetLevel_returns_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithAttributes(level: 7).Build();

            Assert.Equal(7, sys.GetLevel(id));
        }

        [Fact]
        public void SetLevel_updates_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithAttributes(level: 1).Build();

            sys.SetLevel(id, 10);

            Assert.Equal(10, sys.GetLevel(id));
        }

        [Fact]
        public void GetMind_returns_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithAttributes(mind: 15).Build();

            Assert.Equal(15, sys.GetMind(id));
        }

        [Fact]
        public void SetMind_updates_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithAttributes(mind: 10).Build();

            sys.SetMind(id, 18);

            Assert.Equal(18, sys.GetMind(id));
        }

        [Fact]
        public void GetBody_returns_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithAttributes(body: 14).Build();

            Assert.Equal(14, sys.GetBody(id));
        }

        [Fact]
        public void SetBody_updates_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithAttributes(body: 10).Build();

            sys.SetBody(id, 16);

            Assert.Equal(16, sys.GetBody(id));
        }

        [Fact]
        public void GetSpirit_returns_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithAttributes(spirit: 8).Build();

            Assert.Equal(8, sys.GetSpirit(id));
        }

        [Fact]
        public void SetSpirit_updates_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithAttributes(spirit: 10).Build();

            sys.SetSpirit(id, 12);

            Assert.Equal(12, sys.GetSpirit(id));
        }

        [Fact]
        public void GetAttunement_returns_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithAttributes(attunement: 13).Build();

            Assert.Equal(13, sys.GetAttunement(id));
        }

        [Fact]
        public void SetAttunement_updates_stored_value()
        {
            var (sys, ecs) = Build();
            var id = new EntityBuilder(ecs).WithAttributes(attunement: 10).Build();

            sys.SetAttunement(id, 20);

            Assert.Equal(20, sys.GetAttunement(id));
        }

        // ── Missing component — getters return defaults; setters are no-ops ───

        [Fact]
        public void GetCurrentHp_returns_default_100_when_entity_has_no_PoolsComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;  // no pools component added

            Assert.Equal(100, sys.GetCurrentHp(id));
        }

        [Fact]
        public void GetMaxHp_returns_default_100_when_entity_has_no_PoolsComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            Assert.Equal(100, sys.GetMaxHp(id));
        }

        [Fact]
        public void GetCurrentMana_returns_default_50_when_entity_has_no_PoolsComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            Assert.Equal(50, sys.GetCurrentMana(id));
        }

        [Fact]
        public void GetMaxMana_returns_default_50_when_entity_has_no_PoolsComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            Assert.Equal(50, sys.GetMaxMana(id));
        }

        [Fact]
        public void GetCurrentStamina_returns_default_50_when_entity_has_no_PoolsComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            Assert.Equal(50, sys.GetCurrentStamina(id));
        }

        [Fact]
        public void GetMaxStamina_returns_default_50_when_entity_has_no_PoolsComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            Assert.Equal(50, sys.GetMaxStamina(id));
        }

        [Fact]
        public void GetCurrentAstra_returns_default_10_when_entity_has_no_PoolsComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            Assert.Equal(10, sys.GetCurrentAstra(id));
        }

        [Fact]
        public void GetMaxAstra_returns_default_10_when_entity_has_no_PoolsComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            Assert.Equal(10, sys.GetMaxAstra(id));
        }

        [Fact]
        public void GetLevel_returns_default_1_when_entity_has_no_AttributesComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            Assert.Equal(1, sys.GetLevel(id));
        }

        [Fact]
        public void GetMind_returns_default_10_when_entity_has_no_AttributesComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            Assert.Equal(10, sys.GetMind(id));
        }

        [Fact]
        public void SetCurrentHp_is_noop_when_entity_has_no_PoolsComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            // Should not throw; default is returned on read.
            sys.SetCurrentHp(id, 50);

            Assert.Equal(100, sys.GetCurrentHp(id));
        }

        [Fact]
        public void SetMaxHp_is_noop_when_entity_has_no_PoolsComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            sys.SetMaxHp(id, 200);

            Assert.Equal(100, sys.GetMaxHp(id));  // still returns default
        }

        [Fact]
        public void SetLevel_is_noop_when_entity_has_no_AttributesComponent()
        {
            var (sys, ecs) = Build();
            var id = ecs.CreateEntity().Id;

            sys.SetLevel(id, 99);

            Assert.Equal(1, sys.GetLevel(id));  // still returns default
        }

        // ── INV-5: AttributeSystem must not hold IEventBus ───────────────────

        [Fact]
        public void AttributeSystem_does_not_hold_IEventBus_field()
        {
            var type = typeof(AttributeSystem);
            var fields = type.GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: AttributeSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }
    }
}
