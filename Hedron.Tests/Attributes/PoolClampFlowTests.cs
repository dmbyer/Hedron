using System;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Handlers;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Death.Events;
using Hedron.Core.Modules.Death.Systems;
using Hedron.Core.Modules.Effects.Handlers;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Components;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Modules.Time.Events;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Attributes
{
    /// <summary>
    /// Tier 3 — the clamp-down and effective-cap journeys (combat-effective-scores T17–T19), on a
    /// dispatching bus with the real heartbeat handlers: a lapsed max bonus is visible at once and
    /// written back within the tick, a gear swap never refills, and respawn restores a fraction of
    /// the effective cap including progression.
    /// </summary>
    public sealed class PoolClampFlowTests
    {
        private sealed class World
        {
            public EntityService Ecs { get; } = new();
            public RecordingEventBus Bus { get; } = new(dispatch: true);
            public ProgressionSystem Progression { get; }
            public EffectSystem Effects { get; }
            public AttributeSystem Attributes { get; }
            public StatSystem Stats { get; }
            public DeathSystem Death { get; }

            public World()
            {
                Progression = new ProgressionSystem(Ecs, new FakeRandom(1), new AdvancementRuleRegistry());
                Effects = new EffectSystem(Ecs, new IEffectContributor[]
                {
                    new EquipmentEffectContributor(Ecs),
                    new ProgressionEffectContributor(Progression),
                });
                var options = Options.Create(new DeathOptions { HpFloor = -10, RespawnPoolPercent = 0.25, BleedPerTick = 1 });
                Attributes = new AttributeSystem(Ecs, Effects, options);
                Stats = new StatSystem(Attributes, Effects);
                var registry = new TemplateRegistry(Ecs);
                Death = new DeathSystem(
                    Ecs, new EntityStateService(Ecs), Attributes, Effects, registry,
                    new WorldConfiguration { StartingRoom = ResolvedStartingRoom.Unresolved },
                    options, NullLogger<DeathSystem>.Instance);

                // The live heartbeat order: effect expiry (p=20) then the reconcile (p=30).
                Bus.Subscribe<HeartbeatTickEvent>(new EffectTickHandler(Ecs, Effects, Attributes, Death, Bus));
                Bus.Subscribe<HeartbeatTickEvent>(new PoolReconcileTickHandler(Attributes));
            }

            public Task TickAsync(long id, double elapsedMs = 1000) => Bus.PublishAsync(Ticks.At(id, elapsedMs));

            public int StoredHp(uint id) => Ecs.Get<PoolsComponent>(id).CurrentHp;
        }

        [Fact]
        public async Task Expired_buff_clamps_at_once_and_is_written_back_within_a_tick()
        {
            var w = new World();
            var player = new EntityBuilder(w.Ecs).AsPlayer().WithAttributes().WithPools(hp: 100).Build();
            TestEffects.ApplyStatModifier(w.Effects, player, ScoreId.HpMax, 30, durationSeconds: 1.5f);
            w.Attributes.SetCurrentHp(player, 125);
            Assert.Equal(125, w.Stats.Get(player, ScoreId.HpCurrent));

            await w.TickAsync(1); // 1.0 s elapsed: still active
            Assert.Equal(125, w.StoredHp(player));

            await w.TickAsync(2); // 2.0 s: expires in EffectTickHandler, reconciled in the same tick

            Assert.Equal(100, w.Stats.Get(player, ScoreId.HpCurrent));
            Assert.Equal(100, w.Stats.Get(player, ScoreId.HpMax));
            Assert.Equal(100, w.StoredHp(player));
            Assert.DoesNotContain(w.Bus.Published, e => e is PlayerIncapacitatedEvent);
        }

        [Fact]
        public async Task Remove_and_rewear_gives_no_free_refill()
        {
            var w = new World();
            var amulet = w.Ecs.CreateEntity().Id;
            w.Ecs.AddComponent(amulet, new ItemDataComponent
            {
                Name = "amulet",
                StatBonuses = new() { new EquipmentStatBonus(ScoreId.HpMax, 30) },
            });
            var player = new EntityBuilder(w.Ecs).AsPlayer().WithAttributes().WithPools(hp: 100).Build();
            var equipment = new EquipmentComponent();
            equipment.Slots[WornSlot.Neck] = amulet;
            w.Ecs.AddComponent(player, equipment);
            w.Attributes.RestoreToFraction(player, ResourceType.Hp, 1.0);
            Assert.Equal(130, w.StoredHp(player));

            equipment.Slots.Remove(WornSlot.Neck); // remove
            Assert.Equal(100, w.Stats.Get(player, ScoreId.HpCurrent)); // clamped read at once
            await w.TickAsync(1);
            Assert.Equal(100, w.StoredHp(player));                      // written back

            equipment.Slots[WornSlot.Neck] = amulet; // re-wear
            Assert.Equal(130, w.Stats.Get(player, ScoreId.HpMax));
            Assert.Equal(100, w.Stats.Get(player, ScoreId.HpCurrent));  // no refill
        }

        [Fact]
        public void Respawn_after_death_uses_effective_cap()
        {
            var w = new World();
            var player = new EntityBuilder(w.Ecs).AsPlayer().WithAttributes().WithPools(hp: 100).Build();
            w.Ecs.AddComponent(player, new ProgressionComponent
            {
                Improvements =
                {
                    [ProgressionTrack.Of(ScoreId.Body)] = 5,
                    [ProgressionTrack.Of(ScoreId.HpMax)] = 10,
                },
            });
            var cap = w.Stats.Get(player, ScoreId.HpMax);
            Assert.Equal(100 + 10 * ProgressionConstants.PowerPerImprovement, cap);
            w.Attributes.SetCurrentHp(player, -10);
            Assert.Equal(DeathTransition.Died, w.Death.OnHpChanged(player, 0, -10));

            w.Death.Respawn(player);

            Assert.Equal((int)Math.Floor(cap * 0.25), w.Attributes.GetCurrentHp(player));
        }
    }
}
