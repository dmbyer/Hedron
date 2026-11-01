using System;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Attributes
{
    /// <summary>
    /// Tier 1 — the <see cref="IAttributeSystem"/> pool-cap seam (combat-effective-scores T7–T12):
    /// effective max, clamped reads, effective-cap writes, base-max writes, restore-to-fraction and
    /// reconcile — across all four pools, over a real <see cref="EffectSystem"/> so the
    /// <c>&lt;Pool&gt;Max</c> modifiers come through the same path buffs and contributors use.
    /// </summary>
    public sealed class AttributePoolCapTests
    {
        private static (AttributeSystem attributes, EffectSystem effects, EntityService ecs) Build()
        {
            var ecs = new EntityService();
            var effects = new EffectSystem(ecs, Array.Empty<IEffectContributor>());
            return (new AttributeSystem(ecs, effects, Options.Create(new DeathOptions { HpFloor = -10 })), effects, ecs);
        }

        private static ScoreId MaxScore(ResourceType pool) => pool switch
        {
            ResourceType.Hp => ScoreId.HpMax,
            ResourceType.Mana => ScoreId.ManaMax,
            ResourceType.Stamina => ScoreId.StaminaMax,
            _ => ScoreId.AstraMax,
        };

        // Every pool seeded at base 100 so one set of numbers covers all four.
        private static uint Entity(EntityService ecs, int current = 100)
        {
            var id = ecs.CreateEntity().Id;
            ecs.AddComponent(id, new PoolsComponent
            {
                MaxHp = 100, CurrentHp = current,
                MaxMana = 100, CurrentMana = current,
                MaxStamina = 100, CurrentStamina = current,
                MaxAstra = 100, CurrentAstra = current,
            });
            return id;
        }

        private static int Stored(EntityService ecs, uint id, ResourceType pool)
        {
            var p = ecs.Get<PoolsComponent>(id);
            return pool switch
            {
                ResourceType.Hp => p.CurrentHp,
                ResourceType.Mana => p.CurrentMana,
                ResourceType.Stamina => p.CurrentStamina,
                _ => p.CurrentAstra,
            };
        }

        public static TheoryData<ResourceType> Pools => new()
        {
            ResourceType.Hp, ResourceType.Mana, ResourceType.Stamina, ResourceType.Astra,
        };

        // ── T7 — GetEffectiveMax ─────────────────────────────────────────────

        [Theory, MemberData(nameof(Pools))]
        public void GetEffectiveMax_is_base_plus_modifiers_floored_at_one(ResourceType pool)
        {
            var (attributes, effects, ecs) = Build();
            var id = Entity(ecs);
            Assert.Equal(100, attributes.GetEffectiveMax(id, pool));

            TestEffects.ApplyStatModifier(effects, id, MaxScore(pool), 30, effectId: "up");
            Assert.Equal(130, attributes.GetEffectiveMax(id, pool));

            TestEffects.ApplyStatModifier(effects, id, MaxScore(pool), -10_000, effectId: "down");
            Assert.Equal(1, attributes.GetEffectiveMax(id, pool));
        }

        [Theory, MemberData(nameof(Pools))]
        public void GetEffectiveMax_floors_at_zero_when_base_is_zero(ResourceType pool)
        {
            var (attributes, effects, ecs) = Build();
            var id = Entity(ecs);
            attributes.SetMax(id, pool, 0);
            TestEffects.ApplyStatModifier(effects, id, MaxScore(pool), -5);
            Assert.Equal(0, attributes.GetEffectiveMax(id, pool));
        }

        // ── T8 — clamped read ────────────────────────────────────────────────

        [Theory, MemberData(nameof(Pools))]
        public void GetCurrent_is_clamped_read(ResourceType pool)
        {
            var (attributes, effects, ecs) = Build();
            var id = Entity(ecs);
            TestEffects.ApplyStatModifier(effects, id, MaxScore(pool), 30, durationSeconds: 5f);
            attributes.SetCurrent(id, pool, 125);

            effects.AdvanceTick(TimeSpan.FromSeconds(10)); // the buff expires; storage still says 125

            Assert.Equal(125, Stored(ecs, id, pool));
            Assert.Equal(100, attributes.GetCurrent(id, pool));
            var typed = pool switch
            {
                ResourceType.Hp => attributes.GetCurrentHp(id),
                ResourceType.Mana => attributes.GetCurrentMana(id),
                ResourceType.Stamina => attributes.GetCurrentStamina(id),
                _ => attributes.GetCurrentAstra(id),
            };
            Assert.Equal(100, typed);
        }

        [Fact]
        public void GetCurrent_negative_hp_reads_unchanged()
        {
            var (attributes, _, ecs) = Build();
            var id = Entity(ecs);
            attributes.SetCurrentHp(id, -5);
            Assert.Equal(-5, attributes.GetCurrent(id, ResourceType.Hp));
        }

        // ── T9 — writes clamp to the effective cap ───────────────────────────

        [Theory, MemberData(nameof(Pools))]
        public void SetCurrent_clamps_to_effective_cap(ResourceType pool)
        {
            var (attributes, effects, ecs) = Build();
            var id = Entity(ecs);
            TestEffects.ApplyStatModifier(effects, id, MaxScore(pool), 30);

            attributes.SetCurrent(id, pool, 200);
            Assert.Equal(130, Stored(ecs, id, pool));

            attributes.SetCurrent(id, pool, -50);
            Assert.Equal(pool == ResourceType.Hp ? -10 : 0, Stored(ecs, id, pool));
        }

        // ── T10 — SetMax writes the base, clamps current to the effective cap ─

        [Theory, MemberData(nameof(Pools))]
        public void SetMax_clamps_current_to_effective_cap(ResourceType pool)
        {
            var (attributes, effects, ecs) = Build();
            var id = Entity(ecs);
            TestEffects.ApplyStatModifier(effects, id, MaxScore(pool), 30);
            attributes.SetCurrent(id, pool, 130);

            attributes.SetMax(id, pool, 80);

            Assert.Equal(80, pool switch
            {
                ResourceType.Hp => attributes.GetBaseMaxHp(id),
                ResourceType.Mana => attributes.GetBaseMaxMana(id),
                ResourceType.Stamina => attributes.GetBaseMaxStamina(id),
                _ => attributes.GetBaseMaxAstra(id),
            });
            Assert.Equal(110, Stored(ecs, id, pool)); // 80 + 30, not the raw 80
        }

        // ── T11 — RestoreToFraction ──────────────────────────────────────────

        [Theory, MemberData(nameof(Pools))]
        public void RestoreToFraction_uses_effective_cap(ResourceType pool)
        {
            var (attributes, effects, ecs) = Build();
            var id = Entity(ecs, current: 10);
            TestEffects.ApplyStatModifier(effects, id, MaxScore(pool), 40);

            attributes.RestoreToFraction(id, pool, 0.25);
            Assert.Equal(35, Stored(ecs, id, pool)); // floor(140 * 0.25)

            attributes.RestoreToFraction(id, pool, 1.0);
            Assert.Equal(140, Stored(ecs, id, pool));
        }

        [Fact]
        public void RestoreToFraction_at_floor_one_cap_rounds_down_to_zero()
        {
            var (attributes, effects, ecs) = Build();
            var id = Entity(ecs);
            TestEffects.ApplyStatModifier(effects, id, ScoreId.ManaMax, -10_000);
            attributes.RestoreToFraction(id, ResourceType.Mana, 0.25);
            Assert.Equal(0, Stored(ecs, id, ResourceType.Mana));
        }

        // ── T12 — reconcile ──────────────────────────────────────────────────

        [Theory, MemberData(nameof(Pools))]
        public void ReconcilePools_writes_stored_down_to_cap_only(ResourceType pool)
        {
            var (attributes, effects, ecs) = Build();
            var id = Entity(ecs);
            TestEffects.ApplyStatModifier(effects, id, MaxScore(pool), 30, durationSeconds: 5f);
            attributes.SetCurrent(id, pool, 130);
            Assert.False(attributes.ReconcilePools(id)); // nothing above the cap yet

            effects.AdvanceTick(TimeSpan.FromSeconds(10));

            Assert.True(attributes.ReconcilePools(id));
            Assert.Equal(100, Stored(ecs, id, pool));
        }

        [Fact]
        public void ReconcilePools_never_raises_and_leaves_negative_hp()
        {
            var (attributes, effects, ecs) = Build();
            var id = Entity(ecs, current: 40);
            attributes.SetCurrentHp(id, -5);
            TestEffects.ApplyStatModifier(effects, id, ScoreId.HpMax, 30);

            Assert.False(attributes.ReconcilePools(id));
            Assert.Equal(-5, Stored(ecs, id, ResourceType.Hp));
            Assert.Equal(40, Stored(ecs, id, ResourceType.Mana));
        }

        [Fact]
        public void ReconcileAllPools_returns_number_of_values_corrected()
        {
            var (attributes, effects, ecs) = Build();
            var a = Entity(ecs);
            var b = Entity(ecs);
            var untouched = Entity(ecs);
            foreach (var (id, score) in new[] { (a, ScoreId.HpMax), (a, ScoreId.ManaMax), (b, ScoreId.AstraMax) })
                TestEffects.ApplyStatModifier(effects, id, score, 20, durationSeconds: 5f);
            attributes.SetCurrent(a, ResourceType.Hp, 120);
            attributes.SetCurrent(a, ResourceType.Mana, 120);
            attributes.SetCurrent(b, ResourceType.Astra, 120);

            effects.AdvanceTick(TimeSpan.FromSeconds(10));

            Assert.Equal(3, attributes.ReconcileAllPools());
            Assert.Equal(0, attributes.ReconcileAllPools());
            Assert.Equal(100, Stored(ecs, untouched, ResourceType.Hp));
        }
    }
}
