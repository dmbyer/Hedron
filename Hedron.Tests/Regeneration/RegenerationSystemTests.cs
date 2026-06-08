using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Regeneration.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Regeneration
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="RegenerationSystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/resource-regeneration.md.
    /// State-based regeneration rates:
    ///   InCombat  → no regen (suppressed entirely)
    ///   Resting   → +RegenAmount (=1) to each pool every tick
    ///   Idle      → +RegenAmount (=1) to each pool every IdleIntervalTicks-th tick (tickId % 3 == 0)
    ///   Full pool → no-op (IAttributeSystem clamps at max)
    /// </summary>
    public sealed class RegenerationSystemTests
    {
        // ── Private constants mirroring RegenerationSystem internals ─────────────

        /// <summary>Must match <c>RegenerationSystem.IdleIntervalTicks</c>.</summary>
        private const long IdleIntervalTicks = 3;

        /// <summary>Must match <c>RegenerationSystem.RegenAmount</c>.</summary>
        private const int RegenAmount = 1;

        // ── Build helper ─────────────────────────────────────────────────────────

        /// <summary>
        /// Wires <see cref="RegenerationSystem"/> against real collaborators backed by a
        /// single shared <see cref="EntityService"/>. No mocks — all stubs are hand-rolled
        /// or real objects.
        /// </summary>
        private static (RegenerationSystem regen,
                         AttributeSystem attributes,
                         EntityStateService stateService,
                         EntityService ecs) Build()
        {
            var ecs = new EntityService();
            var noEffects = new EffectSystem(ecs, Array.Empty<IEffectContributor>());
            var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
            var attributes = new AttributeSystem(ecs, noEffects, deathOpts);
            var stateService = new EntityStateService(ecs);
            var regen = new RegenerationSystem(ecs, stateService, attributes);
            return (regen, attributes, stateService, ecs);
        }

        /// <summary>
        /// Creates a player with depleted pools (current = max - 10) so regen has room to act.
        /// </summary>
        private static uint MakeIdlePlayerLow(EntityService ecs, int hp = 100, int mana = 50,
                                              int stamina = 50, int astra = 10)
            => new EntityBuilder(ecs)
                .AsPlayer()
                .With(new PoolsComponent
                {
                    MaxHp = hp,      CurrentHp = hp - 10,
                    MaxMana = mana,  CurrentMana = mana - 10,
                    MaxStamina = stamina, CurrentStamina = stamina - 10,
                    MaxAstra = astra, CurrentAstra = Math.Max(0, astra - 10),
                })
                .Build();

        // ── Idle regen: every IdleIntervalTicks-th tick ──────────────────────────

        [Fact]
        public void Idle_entity_gains_RegenAmount_to_HP_on_interval_tick()
        {
            var (regen, _, _, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs);
            var before = ecs.Get<PoolsComponent>(id).CurrentHp;

            regen.ApplyTickRegen(IdleIntervalTicks); // tickId % 3 == 0

            Assert.Equal(before + RegenAmount, ecs.Get<PoolsComponent>(id).CurrentHp);
        }

        [Fact]
        public void Idle_entity_gains_RegenAmount_to_Mana_on_interval_tick()
        {
            var (regen, _, _, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs);
            var before = ecs.Get<PoolsComponent>(id).CurrentMana;

            regen.ApplyTickRegen(IdleIntervalTicks);

            Assert.Equal(before + RegenAmount, ecs.Get<PoolsComponent>(id).CurrentMana);
        }

        [Fact]
        public void Idle_entity_gains_RegenAmount_to_Stamina_on_interval_tick()
        {
            var (regen, _, _, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs);
            var before = ecs.Get<PoolsComponent>(id).CurrentStamina;

            regen.ApplyTickRegen(IdleIntervalTicks);

            Assert.Equal(before + RegenAmount, ecs.Get<PoolsComponent>(id).CurrentStamina);
        }

        [Fact]
        public void Idle_entity_gains_RegenAmount_to_Astra_on_interval_tick()
        {
            var (regen, _, _, ecs) = Build();
            // Give astra room to regen
            var id = new EntityBuilder(ecs)
                .AsPlayer()
                .With(new PoolsComponent { MaxAstra = 20, CurrentAstra = 5 })
                .Build();
            var before = ecs.Get<PoolsComponent>(id).CurrentAstra;

            regen.ApplyTickRegen(IdleIntervalTicks);

            Assert.Equal(before + RegenAmount, ecs.Get<PoolsComponent>(id).CurrentAstra);
        }

        [Fact]
        public void Idle_entity_does_NOT_regen_on_non_interval_tick()
        {
            var (regen, _, _, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs);
            var before = ecs.Get<PoolsComponent>(id).CurrentHp;

            // tickId % 3 != 0 → no regen
            regen.ApplyTickRegen(IdleIntervalTicks + 1);

            Assert.Equal(before, ecs.Get<PoolsComponent>(id).CurrentHp);
        }

        [Fact]
        public void Idle_entity_does_NOT_regen_on_second_non_interval_tick()
        {
            var (regen, _, _, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs);
            var before = ecs.Get<PoolsComponent>(id).CurrentHp;

            regen.ApplyTickRegen(IdleIntervalTicks + 2);

            Assert.Equal(before, ecs.Get<PoolsComponent>(id).CurrentHp);
        }

        [Fact]
        public void Idle_entity_regens_on_every_third_tick_across_multiple_intervals()
        {
            var (regen, _, _, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs, hp: 100);
            var start = ecs.Get<PoolsComponent>(id).CurrentHp;

            // Simulate ticks 1..9; interval ticks are 3, 6, 9 → 3 regen pulses
            for (long t = 1; t <= 9; t++)
                regen.ApplyTickRegen(t);

            Assert.Equal(start + 3 * RegenAmount, ecs.Get<PoolsComponent>(id).CurrentHp);
        }

        // ── Resting regen: every tick ─────────────────────────────────────────────

        [Fact]
        public void Resting_entity_gains_HP_on_every_tick_including_non_interval()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs);
            stateService.TryEnterState(id, EntityStateFlags.Resting, out _);
            var before = ecs.Get<PoolsComponent>(id).CurrentHp;

            // non-interval tick — idle would skip, resting must not
            regen.ApplyTickRegen(IdleIntervalTicks + 1);

            Assert.Equal(before + RegenAmount, ecs.Get<PoolsComponent>(id).CurrentHp);
        }

        [Fact]
        public void Resting_entity_gains_Mana_on_every_tick()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs);
            stateService.TryEnterState(id, EntityStateFlags.Resting, out _);
            var before = ecs.Get<PoolsComponent>(id).CurrentMana;

            regen.ApplyTickRegen(IdleIntervalTicks + 1);

            Assert.Equal(before + RegenAmount, ecs.Get<PoolsComponent>(id).CurrentMana);
        }

        [Fact]
        public void Resting_entity_gains_Stamina_on_every_tick()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs);
            stateService.TryEnterState(id, EntityStateFlags.Resting, out _);
            var before = ecs.Get<PoolsComponent>(id).CurrentStamina;

            regen.ApplyTickRegen(IdleIntervalTicks + 1);

            Assert.Equal(before + RegenAmount, ecs.Get<PoolsComponent>(id).CurrentStamina);
        }

        [Fact]
        public void Resting_entity_gains_Astra_on_every_tick()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = new EntityBuilder(ecs)
                .AsPlayer()
                .With(new PoolsComponent { MaxAstra = 20, CurrentAstra = 5 })
                .Build();
            stateService.TryEnterState(id, EntityStateFlags.Resting, out _);
            var before = ecs.Get<PoolsComponent>(id).CurrentAstra;

            regen.ApplyTickRegen(IdleIntervalTicks + 1);

            Assert.Equal(before + RegenAmount, ecs.Get<PoolsComponent>(id).CurrentAstra);
        }

        [Fact]
        public void Resting_entity_regens_on_ALL_ticks_including_interval_tick()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs, hp: 100);
            stateService.TryEnterState(id, EntityStateFlags.Resting, out _);
            var start = ecs.Get<PoolsComponent>(id).CurrentHp;

            // 3 consecutive ticks — all must produce regen (regardless of interval alignment)
            regen.ApplyTickRegen(1);
            regen.ApplyTickRegen(2);
            regen.ApplyTickRegen(3);

            Assert.Equal(start + 3 * RegenAmount, ecs.Get<PoolsComponent>(id).CurrentHp);
        }

        // ── InCombat suppression ─────────────────────────────────────────────────

        [Fact]
        public void InCombat_entity_receives_NO_HP_regen_on_interval_tick()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs);
            stateService.TryEnterState(id, EntityStateFlags.InCombat, out _);
            var before = ecs.Get<PoolsComponent>(id).CurrentHp;

            regen.ApplyTickRegen(IdleIntervalTicks);

            Assert.Equal(before, ecs.Get<PoolsComponent>(id).CurrentHp);
        }

        [Fact]
        public void InCombat_entity_receives_NO_Mana_regen_on_interval_tick()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs);
            stateService.TryEnterState(id, EntityStateFlags.InCombat, out _);
            var before = ecs.Get<PoolsComponent>(id).CurrentMana;

            regen.ApplyTickRegen(IdleIntervalTicks);

            Assert.Equal(before, ecs.Get<PoolsComponent>(id).CurrentMana);
        }

        [Fact]
        public void InCombat_entity_receives_NO_regen_on_any_tick()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = MakeIdlePlayerLow(ecs, hp: 100);
            stateService.TryEnterState(id, EntityStateFlags.InCombat, out _);
            var start = ecs.Get<PoolsComponent>(id).CurrentHp;

            for (long t = 1; t <= 10; t++)
                regen.ApplyTickRegen(t);

            Assert.Equal(start, ecs.Get<PoolsComponent>(id).CurrentHp);
        }

        // ── Pool max clamp: already-full pools do not over-heal ──────────────────

        [Fact]
        public void Idle_full_HP_pool_does_not_exceed_max()
        {
            var (regen, _, _, ecs) = Build();
            var id = new EntityBuilder(ecs)
                .AsPlayer()
                .With(new PoolsComponent { MaxHp = 50, CurrentHp = 50 }) // already full
                .Build();

            regen.ApplyTickRegen(IdleIntervalTicks);

            var pools = ecs.Get<PoolsComponent>(id);
            Assert.Equal(50, pools.CurrentHp);
            Assert.Equal(pools.MaxHp, pools.CurrentHp);
        }

        [Fact]
        public void Idle_full_Mana_pool_does_not_exceed_max()
        {
            var (regen, _, _, ecs) = Build();
            var id = new EntityBuilder(ecs)
                .AsPlayer()
                .With(new PoolsComponent { MaxMana = 30, CurrentMana = 30 })
                .Build();

            regen.ApplyTickRegen(IdleIntervalTicks);

            var pools = ecs.Get<PoolsComponent>(id);
            Assert.Equal(30, pools.CurrentMana);
        }

        [Fact]
        public void Resting_full_HP_pool_does_not_exceed_max()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = new EntityBuilder(ecs)
                .AsPlayer()
                .With(new PoolsComponent { MaxHp = 50, CurrentHp = 50 })
                .Build();
            stateService.TryEnterState(id, EntityStateFlags.Resting, out _);

            // Call multiple ticks to confirm clamp holds over time
            regen.ApplyTickRegen(1);
            regen.ApplyTickRegen(2);
            regen.ApplyTickRegen(3);

            var pools = ecs.Get<PoolsComponent>(id);
            Assert.Equal(pools.MaxHp, pools.CurrentHp);
        }

        [Fact]
        public void Resting_full_Mana_pool_does_not_exceed_max()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = new EntityBuilder(ecs)
                .AsPlayer()
                .With(new PoolsComponent { MaxMana = 40, CurrentMana = 40 })
                .Build();
            stateService.TryEnterState(id, EntityStateFlags.Resting, out _);

            regen.ApplyTickRegen(1);

            var pools = ecs.Get<PoolsComponent>(id);
            Assert.Equal(40, pools.CurrentMana);
        }

        [Fact]
        public void Resting_full_Stamina_pool_does_not_exceed_max()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = new EntityBuilder(ecs)
                .AsPlayer()
                .With(new PoolsComponent { MaxStamina = 60, CurrentStamina = 60 })
                .Build();
            stateService.TryEnterState(id, EntityStateFlags.Resting, out _);

            regen.ApplyTickRegen(1);

            var pools = ecs.Get<PoolsComponent>(id);
            Assert.Equal(60, pools.CurrentStamina);
        }

        [Fact]
        public void Resting_full_Astra_pool_does_not_exceed_max()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = new EntityBuilder(ecs)
                .AsPlayer()
                .With(new PoolsComponent { MaxAstra = 15, CurrentAstra = 15 })
                .Build();
            stateService.TryEnterState(id, EntityStateFlags.Resting, out _);

            regen.ApplyTickRegen(1);

            var pools = ecs.Get<PoolsComponent>(id);
            Assert.Equal(15, pools.CurrentAstra);
        }

        // ── Entity without PoolsComponent is skipped (no exception) ─────────────

        [Fact]
        public void Entity_without_PoolsComponent_is_silently_skipped()
        {
            var (regen, _, _, ecs) = Build();
            // A bare entity with no PoolsComponent (e.g. a room entity)
            var bare = ecs.CreateEntity();

            // Must not throw
            var ex = Record.Exception(() => regen.ApplyTickRegen(IdleIntervalTicks));
            Assert.Null(ex);
        }

        // ── Mobs regen out of combat (same sweep as players) ─────────────────────

        [Fact]
        public void Mob_out_of_combat_regens_HP_on_interval_tick()
        {
            var (regen, _, _, ecs) = Build();
            var id = new EntityBuilder(ecs)
                .AsMob("orc")
                .With(new PoolsComponent { MaxHp = 80, CurrentHp = 60 })
                .Build();
            var before = ecs.Get<PoolsComponent>(id).CurrentHp;

            regen.ApplyTickRegen(IdleIntervalTicks);

            Assert.Equal(before + RegenAmount, ecs.Get<PoolsComponent>(id).CurrentHp);
        }

        [Fact]
        public void Mob_in_combat_does_NOT_regen()
        {
            var (regen, _, stateService, ecs) = Build();
            var id = new EntityBuilder(ecs)
                .AsMob("orc")
                .With(new PoolsComponent { MaxHp = 80, CurrentHp = 60 })
                .Build();
            stateService.TryEnterState(id, EntityStateFlags.InCombat, out _);
            var before = ecs.Get<PoolsComponent>(id).CurrentHp;

            regen.ApplyTickRegen(IdleIntervalTicks);

            Assert.Equal(before, ecs.Get<PoolsComponent>(id).CurrentHp);
        }

        // ── Multiple entities in the same sweep ───────────────────────────────────

        [Fact]
        public void Multiple_idle_entities_all_regen_on_interval_tick()
        {
            var (regen, _, _, ecs) = Build();
            var ids = new uint[3];
            for (int i = 0; i < 3; i++)
                ids[i] = MakeIdlePlayerLow(ecs);

            regen.ApplyTickRegen(IdleIntervalTicks);

            foreach (var id in ids)
                Assert.Equal(90 + RegenAmount, ecs.Get<PoolsComponent>(id).CurrentHp);
        }

        [Fact]
        public void Mixed_entities_only_non_combat_ones_regen()
        {
            var (regen, _, stateService, ecs) = Build();
            var idle = MakeIdlePlayerLow(ecs, hp: 100);
            var combatant = MakeIdlePlayerLow(ecs, hp: 100);
            stateService.TryEnterState(combatant, EntityStateFlags.InCombat, out _);

            var idleBefore = ecs.Get<PoolsComponent>(idle).CurrentHp;
            var combatBefore = ecs.Get<PoolsComponent>(combatant).CurrentHp;

            regen.ApplyTickRegen(IdleIntervalTicks);

            Assert.Equal(idleBefore + RegenAmount, ecs.Get<PoolsComponent>(idle).CurrentHp);
            Assert.Equal(combatBefore, ecs.Get<PoolsComponent>(combatant).CurrentHp);
        }

        // ── INV-5: RegenerationSystem does not hold IEventBus ───────────────────

        [Fact]
        public void RegenerationSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(RegenerationSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: RegenerationSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }
    }
}
