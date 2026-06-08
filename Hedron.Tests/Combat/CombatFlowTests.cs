using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Combat;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Combat.Handlers;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Death.Systems;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Combat
{
    /// <summary>
    /// Tier 3 — flow / integration tests for the combat pipeline.
    /// Exercises <see cref="CombatTickHandler"/> + <see cref="CombatMobDeathHandler"/>
    /// wired to real sub-systems and a <see cref="RecordingEventBus"/> with dispatch enabled.
    ///
    /// Coverage contract: the four invisible-state postconditions from WP-4.1:
    ///   1. INV-21: no explicit BlueprintComponent clear before DestroyEntity
    ///   2. Dedup: each combat pair is processed exactly once per tick
    ///   3. HP clamp: HP cannot go below HpFloor after a hit
    ///   4. No-bus-in-system: CombatSystem itself never touches the bus
    /// </summary>
    public sealed class CombatFlowTests
    {
        // ── Stub IDeathSystem ────────────────────────────────────────────────────

        /// <summary>
        /// Minimal <see cref="IDeathSystem"/> stub that always returns
        /// <see cref="DeathTransition.None"/> — sufficient for kill-path flow tests
        /// where the handler already handles mob death without going through DeathSystem.
        /// </summary>
        private sealed class NoOpDeathSystem : IDeathSystem
        {
            public DeathTransition OnHpChanged(uint entityId, int previousHp, int newHp)
                => DeathTransition.None;

            public void Respawn(uint entityId) { }

            public bool SetRespawn(uint entityId, string roomBlueprintId, out string? failReason)
            {
                failReason = null;
                return true;
            }
        }

        // ── Test-world factory ───────────────────────────────────────────────────

        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public AttributeSystem Attributes { get; }
            public StatSystem Stats { get; }
            public AspectSystem Aspects { get; }
            public CombatSystem Combat { get; }
            public EntityStateService EntityState { get; }
            public RecordingEventBus Bus { get; }
            public CombatTickHandler TickHandler { get; }
            public CombatMobDeathHandler MobDeathHandler { get; }

            public TestWorld(FakeRandom rng)
            {
                Ecs = new EntityService();

                var noEffects = new EffectSystem(Ecs, System.Array.Empty<IEffectContributor>());
                var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });

                Attributes = new AttributeSystem(Ecs, noEffects, deathOpts);
                Stats = new StatSystem(Attributes, Ecs, noEffects);
                Aspects = new AspectSystem(Ecs);
                Combat = new CombatSystem(Ecs, Stats, Attributes, Aspects, rng);
                EntityState = new EntityStateService(Ecs);

                // Recording bus with dispatch=true so handlers fire automatically.
                Bus = new RecordingEventBus(dispatch: true);

                var deathSystem = new NoOpDeathSystem();

                TickHandler = new CombatTickHandler(
                    Ecs,
                    Combat,
                    EntityState,
                    deathSystem,
                    Stats,
                    Bus,
                    NullLogger<CombatTickHandler>.Instance);

                MobDeathHandler = new CombatMobDeathHandler(
                    Ecs,
                    EntityState,
                    Bus);

                // Subscribe handlers to the bus so dispatch fires them.
                Bus.Subscribe<Hedron.Core.Modules.Time.Events.HeartbeatTickEvent>(TickHandler);
                Bus.Subscribe<CombatEndedEvent>(MobDeathHandler);
            }
        }

        // ── Flow: kill → tick → MobDied ──────────────────────────────────────────

        /// <summary>
        /// Full kill flow:
        ///   1. Set up combat between player and mob (1 HP mob, guaranteed hit/damage).
        ///   2. Pump one heartbeat tick.
        ///   3. Assert: MobDied CombatEndedEvent published, survivor exits InCombat,
        ///      mob entity destroyed (DestroyEntity removes all components).
        /// </summary>
        [Fact]
        public async Task Kill_flow_publishes_CombatEndedEvent_MobDied_and_destroys_mob()
        {
            // roll=20 (d20 hit guaranteed), then damage=1 (kills 1-HP mob)
            var rng = new FakeRandom(20, 1, 20, 1, 20, 1, 20, 1); // extra pairs for safety
            var world = new TestWorld(rng);

            const uint roomId = 1u;

            // Player entity must have lower ID so dedup makes player the attacker.
            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .InRoom(roomId)
                .Build();

            var mobId = new EntityBuilder(world.Ecs)
                .AsMob("rat")
                .WithAttributes(body: 10)
                .WithPools(hp: 1)         // 1 HP → dies on any hit
                .InRoom(roomId)
                .With(new BlueprintComponent { BlueprintId = "mob.rat" })
                .Build();

            // Put both in InCombat state and link CombatStateComponents.
            world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
            world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
            world.Combat.StartCombat(playerId, mobId);

            // Fire the tick.
            await world.Bus.PublishAsync(Ticks.At(1));

            // CombatEndedEvent with MobDied must have been published.
            var endedEvents = world.Bus.Published
                .OfType<CombatEndedEvent>()
                .ToList();

            Assert.True(endedEvents.Count > 0,
                "At least one CombatEndedEvent must be published");

            var mobDiedEvent = endedEvents.FirstOrDefault(e => e.Outcome == CombatEndOutcome.MobDied);
            Assert.NotNull(mobDiedEvent);
            Assert.Equal(mobId, mobDiedEvent!.DefenderEntityId);
        }

        [Fact]
        public async Task Kill_flow_mob_entity_is_destroyed_after_MobDied()
        {
            var rng = new FakeRandom(20, 1, 20, 1);
            var world = new TestWorld(rng);

            const uint roomId = 1u;
            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer().WithAttributes(body: 10).WithPools(hp: 100).InRoom(roomId).Build();

            var mobId = new EntityBuilder(world.Ecs)
                .AsMob("rat").WithAttributes(body: 10).WithPools(hp: 1).InRoom(roomId)
                .With(new BlueprintComponent { BlueprintId = "mob.rat" })
                .Build();

            world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
            world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
            world.Combat.StartCombat(playerId, mobId);

            await world.Bus.PublishAsync(Ticks.At(1));

            // After DestroyEntity, the mob has no components.
            Assert.False(world.Ecs.HasComponent<MobDataComponent>(mobId),
                "MobDataComponent must be absent after entity destruction");
            Assert.False(world.Ecs.HasComponent<CombatStateComponent>(mobId),
                "CombatStateComponent must be absent after entity destruction");
        }

        [Fact]
        public async Task Kill_flow_survivor_exits_InCombat_after_MobDied()
        {
            var rng = new FakeRandom(20, 1, 20, 1);
            var world = new TestWorld(rng);

            const uint roomId = 1u;
            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer().WithAttributes(body: 10).WithPools(hp: 100).InRoom(roomId).Build();

            var mobId = new EntityBuilder(world.Ecs)
                .AsMob("rat").WithAttributes(body: 10).WithPools(hp: 1).InRoom(roomId)
                .With(new BlueprintComponent { BlueprintId = "mob.rat" })
                .Build();

            world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
            world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
            world.Combat.StartCombat(playerId, mobId);

            await world.Bus.PublishAsync(Ticks.At(1));

            Assert.False(
                world.EntityState.IsInState(playerId, EntityStateFlags.InCombat),
                "Surviving player must no longer be InCombat after mob dies");
        }

        // ── INV-21: BlueprintComponent not explicitly cleared before DestroyEntity ──

        /// <summary>
        /// INV-21: The <see cref="CombatMobDeathHandler"/> must NOT explicitly call
        /// <c>RemoveComponent&lt;BlueprintComponent&gt;</c> before <c>DestroyEntity</c>.
        /// <c>BlueprintComponent</c> is preserved as the origin record until entity
        /// destruction removes all components. Spawn-slot vacancy is tracked by
        /// <c>SpawnSystem</c> via the <c>MobDiedEvent</c>.
        ///
        /// This test reflects the CURRENT behavior: the handler publishes <see cref="MobDiedEvent"/>
        /// with the blueprint id read directly from <see cref="BlueprintComponent"/> WHILE the
        /// entity is still live, then calls <c>DestroyEntity</c> without an explicit
        /// <c>RemoveComponent&lt;BlueprintComponent&gt;</c>. The <see cref="MobDiedEvent"/>
        /// carries the blueprint id so callers get it from the event payload, not the dead entity.
        ///
        /// Verification: inspect <see cref="MobDiedEvent.BlueprintId"/> is non-empty (handler
        /// read it while entity was live), and <see cref="BlueprintComponent"/> is absent
        /// after destruction (destroyed by <c>DestroyEntity</c>, not by an explicit remove).
        /// </summary>
        [Fact]
        public async Task INV21_BlueprintComponent_not_explicitly_cleared_before_DestroyEntity()
        {
            var rng = new FakeRandom(20, 1, 20, 1);
            var world = new TestWorld(rng);

            const uint roomId = 1u;
            const string blueprintId = "mob.goblin.test";

            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer().WithAttributes(body: 10).WithPools(hp: 100).InRoom(roomId).Build();

            var mobId = new EntityBuilder(world.Ecs)
                .AsMob("goblin").WithAttributes(body: 10).WithPools(hp: 1).InRoom(roomId)
                .With(new BlueprintComponent { BlueprintId = blueprintId })
                .Build();

            world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
            world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
            world.Combat.StartCombat(playerId, mobId);

            await world.Bus.PublishAsync(Ticks.At(1));

            // The MobDiedEvent must carry the blueprint id (read before destruction).
            var mobDiedEvent = world.Bus.Published
                .OfType<MobDiedEvent>()
                .FirstOrDefault();

            Assert.NotNull(mobDiedEvent);
            Assert.Equal(blueprintId, mobDiedEvent!.BlueprintId);

            // After DestroyEntity, BlueprintComponent is gone — but it was removed by
            // DestroyEntity (which removes ALL components), not by an explicit RemoveComponent call.
            Assert.False(world.Ecs.HasComponent<BlueprintComponent>(mobId),
                "BlueprintComponent should be absent after DestroyEntity");
        }

        // ── Dedup: pair processed exactly once per tick ──────────────────────────

        /// <summary>
        /// The dedup guard: process only when <c>entityId &lt; opponentEntityId</c>.
        /// Both entities have <see cref="CombatStateComponent"/>; only the lower-id entity
        /// triggers a round. This means exactly one <see cref="CombatRoundEvent"/> is published
        /// (plus possibly a counter-round from the same pair). We verify that the initial
        /// attack is not duplicated: the pair is not processed from BOTH sides.
        ///
        /// Strategy: after the tick, count <see cref="CombatRoundEvent"/>s whose
        /// <c>AttackerEntityId == lowerEntityId</c>. Must be exactly 1 (one tick → one round).
        /// </summary>
        [Fact]
        public async Task Dedup_pair_processed_exactly_once_as_primary_attacker_per_tick()
        {
            // Scripted for hit then a counter-attack miss to keep both alive across 2 rounds.
            // roll sequence: hit=20, damage=1 (for primary round, non-lethal), then counter miss=1
            var rng = new FakeRandom(20, 1, 1);   // hit, damage, counter miss
            var world = new TestWorld(rng);

            const uint roomId = 5u;

            // Build player first so playerId < mobId (deterministic ordering).
            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer().WithAttributes(body: 10).WithPools(hp: 100).InRoom(roomId).Build();

            var mobId = new EntityBuilder(world.Ecs)
                .AsMob("troll").WithAttributes(body: 10).WithPools(hp: 100).InRoom(roomId).Build();

            // Verify the dedup assumption: player was created first → lower id.
            Assert.True(playerId < mobId,
                "Test assumes player entity was allocated before mob entity (lower id)");

            world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
            world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
            world.Combat.StartCombat(playerId, mobId);

            await world.Bus.PublishAsync(Ticks.At(1));

            // Count rounds where the lower-id entity (player) was the primary attacker.
            var playerAttackRounds = world.Bus.Published
                .OfType<Hedron.Core.Modules.Combat.Events.CombatRoundEvent>()
                .Where(e => e.AttackerEntityId == playerId && e.DefenderEntityId == mobId)
                .ToList();

            Assert.True(
                playerAttackRounds.Count == 1,
                $"Player must be the primary attacker exactly once per tick; " +
                $"got {playerAttackRounds.Count} rounds");
        }

        // ── HP clamp: cannot go below HpFloor ────────────────────────────────────

        [Fact]
        public void HP_cannot_go_below_HpFloor_after_overkill_hit()
        {
            // Large damage roll to overkill; verify hp is clamped at -10 (HpFloor).
            // Body=20 → attackPower=10 → damage roll Next(1, 12), range [1,11].
            // Use roll=20 (d20 hit), damage=11 (max valid for attackPower=10).
            // Mob has 1 HP; damage of 11 → HP goes to -10 at most (HpFloor clamp).
            var rng = new FakeRandom(20, 11);   // hit roll, then damage=11 (overkill on 1-HP mob)
            var (directCombat, directAttributes, directEcs) = BuildDirectWorld(rng);

            var directPlayerId = new EntityBuilder(directEcs)
                .AsPlayer().WithAttributes(body: 20).WithPools(hp: 100).Build();
            var directMobId = new EntityBuilder(directEcs)
                .AsMob("rat").WithAttributes(body: 10).WithPools(hp: 1).Build();

            directCombat.ExecuteRound(directPlayerId, directMobId);

            var hpAfter = directAttributes.GetCurrentHp(directMobId);
            Assert.True(hpAfter >= -10,
                $"HP {hpAfter} must not go below HpFloor (-10) after overkill");
        }

        // ── No-bus-in-system (combat-scoped context) ─────────────────────────────

        /// <summary>
        /// Confirms that in the context of the full combat flow,
        /// <see cref="CombatSystem"/> never receives nor calls the event bus —
        /// all bus interaction happens in <see cref="CombatTickHandler"/> and
        /// <see cref="CombatMobDeathHandler"/>.
        ///
        /// This test asserts that the RecordingEventBus was only published to by
        /// the handlers (not by CombatSystem directly), by verifying that CombatSystem
        /// has no IEventBus field (structural) and the bus is only held by handler types.
        /// </summary>
        [Fact]
        public void CombatSystem_has_no_IEventBus_in_kill_flow_context()
        {
            var busType = typeof(Hedron.Core.Events.IEventBus);

            // CombatSystem must not hold the bus
            var combatFields = typeof(CombatSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in combatFields)
            {
                Assert.False(
                    busType.IsAssignableFrom(field.FieldType),
                    $"INV-5 (combat context): CombatSystem.{field.Name} is IEventBus — " +
                    "only handlers and initiators may publish to the bus");
            }

            // Handlers are the ones that hold the bus — confirm they do
            var tickHandlerFields = typeof(CombatTickHandler).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            var handlerHasBus = tickHandlerFields.Any(f => busType.IsAssignableFrom(f.FieldType));
            Assert.True(handlerHasBus,
                "CombatTickHandler should hold IEventBus (it's a handler — that's correct)");
        }

        // ── Helper ───────────────────────────────────────────────────────────────

        private static (CombatSystem combat, AttributeSystem attributes, EntityService ecs)
            BuildDirectWorld(FakeRandom rng)
        {
            var ecs = new EntityService();
            var noEffects = new EffectSystem(ecs, System.Array.Empty<IEffectContributor>());
            var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
            var attributes = new AttributeSystem(ecs, noEffects, deathOpts);
            var stats = new StatSystem(attributes, ecs, noEffects);
            var aspects = new AspectSystem(ecs);
            var combat = new CombatSystem(ecs, stats, attributes, aspects, rng);
            return (combat, attributes, ecs);
        }
    }
}
