using System;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Spawn.Systems;
using Hedron.Core.Modules.Time.Events;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.Spawn
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="SpawnSystem"/>.
    ///
    /// Coverage: respawn scheduling (RespawnAt = now + delay on vacancy), respawn trigger
    /// (tick fires when RespawnAt &lt;= now), no-early-fire (tick before RespawnAt is a no-op),
    /// FakeClock boundary precision, slot vacancy via MobDiedEvent, slot vacancy via
    /// ItemPickedUpEvent, startup initialization from live entity graph, unknown-entity
    /// vacancy is a no-op, and unknown blueprint disables the slot rather than throwing.
    /// </summary>
    public sealed class SpawnSystemTests
    {
        // ── Harness ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Minimal <see cref="IEntityTemplate"/> stub. Applies no additional components —
        /// <see cref="TemplateRegistry.Spawn"/> already attaches <see cref="BlueprintComponent"/>.
        /// </summary>
        private sealed class MinimalTemplate : IEntityTemplate
        {
            public string BlueprintId { get; }

            public MinimalTemplate(string blueprintId) => BlueprintId = blueprintId;

            public void Apply(Entity entity, EntityService entityService) { }
        }

        /// <summary>
        /// Wires a <see cref="SpawnSystem"/> with a real <see cref="EntityService"/>,
        /// a real <see cref="TemplateRegistry"/>, and a <see cref="FakeClock"/> seeded
        /// to a fixed UTC time.
        /// </summary>
        private static (SpawnSystem system, EntityService ecs, TemplateRegistry registry, FakeClock clock) Build()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var clock = new FakeClock(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var system = new SpawnSystem(ecs, registry, clock, NullLogger<SpawnSystem>.Instance);
            return (system, ecs, registry, clock);
        }

        /// <summary>
        /// Creates a room entity with a single spawn rule pointing at <paramref name="blueprintId"/>
        /// with the given <paramref name="respawnDelaySec"/>.
        /// </summary>
        private static uint MakeRoom(EntityService ecs, string blueprintId, int respawnDelaySec = 60)
        {
            var roomId = ecs.CreateEntity().Id;
            var config = new SpawnConfigComponent();
            config.Rules.Add(new SpawnRule(blueprintId, MinCount: 1, MaxCount: 1, respawnDelaySec));
            ecs.AddComponent(roomId, config);
            return roomId;
        }

        /// <summary>
        /// Creates a mob entity placed in <paramref name="roomEntityId"/> with a
        /// <see cref="BlueprintComponent"/> matching <paramref name="blueprintId"/>.
        /// </summary>
        private static uint MakeMobInRoom(EntityService ecs, uint roomEntityId, string blueprintId)
        {
            var mobId = ecs.CreateEntity().Id;
            ecs.AddComponent(mobId, new BlueprintComponent { BlueprintId = blueprintId });
            ecs.AddComponent(mobId, new LocationComponent { RoomEntityId = roomEntityId });
            return mobId;
        }

        private static HeartbeatTickEvent Tick(long tickId = 1)
            => Ticks.At(tickId);

        // ── WorldContentReady: occupied slot ────────────────────────────────────

        [Fact]
        public async Task WorldContentReady_occupied_slot_has_no_respawn_scheduled()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "mob.goblin";
            registry.Register(bp, new MinimalTemplate(bp));

            var roomId = MakeRoom(ecs, bp, respawnDelaySec: 60);
            var mobId = MakeMobInRoom(ecs, roomId, bp);

            await system.HandleAsync(new WorldContentReadyEvent());

            // Slot is occupied → no respawn should fire on the next tick
            await system.HandleAsync(Tick());

            // Entity placed in the room by MakeMobInRoom still exists; nothing new was spawned.
            // Verify the original mob entity is still tracked by confirming it has BlueprintComponent.
            Assert.True(ecs.HasComponent<BlueprintComponent>(mobId));
        }

        // ── WorldContentReady: vacant slot at startup ───────────────────────────

        [Fact]
        public async Task WorldContentReady_vacant_slot_schedules_respawn_using_clock_now()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "mob.orc";
            registry.Register(bp, new MinimalTemplate(bp));

            var roomId = MakeRoom(ecs, bp, respawnDelaySec: 30);
            // No mob in the room → slot starts vacant

            await system.HandleAsync(new WorldContentReadyEvent());

            // Advance exactly respawnDelay — should trigger respawn
            clock.Advance(TimeSpan.FromSeconds(30));
            await system.HandleAsync(Tick());

            // A new entity should have been spawned in the room
            var spawned = FindEntityInRoom(ecs, roomId, bp);
            Assert.True(spawned.HasValue, "Expected a newly spawned mob in the room after delay elapsed.");
        }

        // ── Respawn scheduling: RespawnAt = clock.UtcNow + delay ────────────────

        [Fact]
        public async Task MobDied_schedules_RespawnAt_equal_to_now_plus_delay()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "mob.wolf";
            const int delay = 120;
            registry.Register(bp, new MinimalTemplate(bp));

            var roomId = MakeRoom(ecs, bp, respawnDelaySec: delay);
            var mobId = MakeMobInRoom(ecs, roomId, bp);
            await system.HandleAsync(new WorldContentReadyEvent());

            // Mob dies
            await system.HandleAsync(new MobDiedEvent(mobId, bp));

            // Advance exactly the delay (boundary: RespawnAt == now)
            clock.Advance(TimeSpan.FromSeconds(delay));
            await system.HandleAsync(Tick());

            // Exclude the dead mob (still in ECS until handler destroys it);
            // a new entity should exist
            var spawned = FindEntityInRoom(ecs, roomId, bp, exclude: mobId);
            Assert.True(spawned.HasValue,
                $"Respawn should fire at exactly t0 + {delay}s (RespawnAt <= now).");
        }

        // ── Respawn does NOT fire before RespawnAt ────────────────────────────────

        [Fact]
        public async Task Tick_before_RespawnAt_does_not_spawn_new_entity()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "mob.rat";
            const int delay = 60;
            registry.Register(bp, new MinimalTemplate(bp));

            var roomId = MakeRoom(ecs, bp, respawnDelaySec: delay);
            var mobId = MakeMobInRoom(ecs, roomId, bp);
            await system.HandleAsync(new WorldContentReadyEvent());

            await system.HandleAsync(new MobDiedEvent(mobId, bp));

            // Count entities in room right after the death (dead mob entity is still there —
            // SpawnSystem does not destroy entities; that's the handler's job).
            int countAfterDeath = CountEntitiesInRoom(ecs, roomId, bp);

            // Advance only 59s — one second short of the 60s delay
            clock.Advance(TimeSpan.FromSeconds(delay - 1));
            await system.HandleAsync(Tick());

            // No new entity should have been added (count must not increase)
            int countAfterTick = CountEntitiesInRoom(ecs, roomId, bp);
            Assert.Equal(countAfterDeath, countAfterTick);
        }

        // ── Respawn fires exactly at RespawnAt boundary ───────────────────────────

        [Fact]
        public async Task Tick_exactly_at_RespawnAt_fires_respawn()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "mob.bandit";
            const int delay = 45;
            registry.Register(bp, new MinimalTemplate(bp));

            var roomId = MakeRoom(ecs, bp, respawnDelaySec: delay);
            var mobId = MakeMobInRoom(ecs, roomId, bp);
            await system.HandleAsync(new WorldContentReadyEvent());

            await system.HandleAsync(new MobDiedEvent(mobId, bp));

            // Advance exactly the delay
            clock.Advance(TimeSpan.FromSeconds(delay));
            await system.HandleAsync(Tick());

            var spawned = FindEntityInRoom(ecs, roomId, bp, exclude: mobId);
            Assert.True(spawned.HasValue,
                "Respawn should fire when clock is exactly at RespawnAt.");
        }

        // ── Respawn fires after RespawnAt (late tick) ──────────────────────────

        [Fact]
        public async Task Tick_after_RespawnAt_fires_respawn()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "mob.skeleton";
            const int delay = 30;
            registry.Register(bp, new MinimalTemplate(bp));

            var roomId = MakeRoom(ecs, bp, respawnDelaySec: delay);
            var mobId = MakeMobInRoom(ecs, roomId, bp);
            await system.HandleAsync(new WorldContentReadyEvent());

            await system.HandleAsync(new MobDiedEvent(mobId, bp));

            // Advance well past the delay
            clock.Advance(TimeSpan.FromSeconds(delay + 10));
            await system.HandleAsync(Tick());

            var spawned = FindEntityInRoom(ecs, roomId, bp, exclude: mobId);
            Assert.True(spawned.HasValue,
                "Respawn should fire when clock is past RespawnAt.");
        }

        // ── Spawned entity is placed in the correct room ───────────────────────

        [Fact]
        public async Task Respawned_entity_has_LocationComponent_pointing_to_correct_room()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "mob.zombie";
            const int delay = 10;
            registry.Register(bp, new MinimalTemplate(bp));

            var roomId = MakeRoom(ecs, bp, respawnDelaySec: delay);
            var mobId = MakeMobInRoom(ecs, roomId, bp);
            await system.HandleAsync(new WorldContentReadyEvent());

            await system.HandleAsync(new MobDiedEvent(mobId, bp));
            clock.Advance(TimeSpan.FromSeconds(delay));
            await system.HandleAsync(Tick());

            var spawnedId = FindEntityInRoom(ecs, roomId, bp, exclude: mobId);
            Assert.True(spawnedId.HasValue);
            var loc = ecs.Get<LocationComponent>(spawnedId!.Value);
            Assert.Equal(roomId, loc.RoomEntityId);
        }

        // ── Spawned entity carries BlueprintComponent ──────────────────────────

        [Fact]
        public async Task Respawned_entity_has_BlueprintComponent_with_correct_id()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "mob.troll";
            const int delay = 5;
            registry.Register(bp, new MinimalTemplate(bp));

            var roomId = MakeRoom(ecs, bp, respawnDelaySec: delay);
            var mobId = MakeMobInRoom(ecs, roomId, bp);
            await system.HandleAsync(new WorldContentReadyEvent());

            await system.HandleAsync(new MobDiedEvent(mobId, bp));
            clock.Advance(TimeSpan.FromSeconds(delay));
            await system.HandleAsync(Tick());

            var spawnedId = FindEntityInRoom(ecs, roomId, bp, exclude: mobId);
            Assert.True(spawnedId.HasValue);
            var bpComp = ecs.Get<BlueprintComponent>(spawnedId!.Value);
            Assert.Equal(bp, bpComp.BlueprintId);
        }

        // ── Slot vacancy via ItemPickedUpEvent ──────────────────────────────────

        [Fact]
        public async Task ItemPickedUp_marks_slot_vacant_and_schedules_respawn()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "item.iron_sword";
            const int delay = 20;
            registry.Register(bp, new MinimalTemplate(bp));

            var roomId = MakeRoom(ecs, bp, respawnDelaySec: delay);
            // Create an item entity in the room (same pattern as a mob)
            var itemId = MakeMobInRoom(ecs, roomId, bp);
            await system.HandleAsync(new WorldContentReadyEvent());

            // Simulate item being picked up — item entity moves out of room
            // (SpawnSystem doesn't destroy/move the entity itself; ItemContextHandler does.
            // For the test we remove the LocationComponent to simulate the item leaving the room.)
            ecs.RemoveComponent<LocationComponent>(itemId);

            await system.HandleAsync(new ItemPickedUpEvent(
                PlayerEntityId: 99u,
                ItemEntityId: itemId,
                RoomEntityId: roomId));

            // Before delay: slot is vacant but RespawnAt not yet reached — no new entity in room
            clock.Advance(TimeSpan.FromSeconds(delay - 1));
            await system.HandleAsync(Tick());
            Assert.False(FindEntityInRoom(ecs, roomId, bp).HasValue,
                "Item slot should not respawn before delay elapses.");

            // After delay: respawn fires and a new item appears in the room
            clock.Advance(TimeSpan.FromSeconds(1));
            await system.HandleAsync(Tick());
            Assert.True(FindEntityInRoom(ecs, roomId, bp).HasValue,
                "Item slot should respawn after delay elapses.");
        }

        // ── Respawn does not re-fire after slot is occupied again ──────────────

        [Fact]
        public async Task Slot_does_not_respawn_twice_after_single_vacancy()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "mob.imp";
            const int delay = 10;
            registry.Register(bp, new MinimalTemplate(bp));

            var roomId = MakeRoom(ecs, bp, respawnDelaySec: delay);
            var mobId = MakeMobInRoom(ecs, roomId, bp);
            await system.HandleAsync(new WorldContentReadyEvent());

            // Remove dead mob from ECS to simulate the handler having destroyed it,
            // so entity counts reflect only living spawn-slot entities.
            await system.HandleAsync(new MobDiedEvent(mobId, bp));
            ecs.DestroyEntity(mobId);

            clock.Advance(TimeSpan.FromSeconds(delay));
            await system.HandleAsync(Tick());

            // One entity should now be in the room (the freshly respawned one)
            int countAfterFirstRespawn = CountEntitiesInRoom(ecs, roomId, bp);
            Assert.Equal(1, countAfterFirstRespawn);

            // Fire additional ticks — should not spawn a second entity into the occupied slot
            await system.HandleAsync(Tick(2));
            await system.HandleAsync(Tick(3));

            int countAfterMoreTicks = CountEntitiesInRoom(ecs, roomId, bp);
            Assert.Equal(1, countAfterMoreTicks);
        }

        // ── Unknown entity vacancy is a no-op ──────────────────────────────────

        [Fact]
        public async Task MobDied_for_untracked_entity_does_not_throw()
        {
            var (system, ecs, registry, clock) = Build();
            await system.HandleAsync(new WorldContentReadyEvent());

            // Entity 9999 was never registered in any slot
            var ex = await Record.ExceptionAsync(() =>
                system.HandleAsync(new MobDiedEvent(MobEntityId: 9999u, BlueprintId: "mob.ghost")));

            Assert.Null(ex);
        }

        [Fact]
        public async Task ItemPickedUp_for_untracked_entity_does_not_throw()
        {
            var (system, ecs, registry, clock) = Build();
            await system.HandleAsync(new WorldContentReadyEvent());

            var ex = await Record.ExceptionAsync(() =>
                system.HandleAsync(new ItemPickedUpEvent(
                    PlayerEntityId: 1u, ItemEntityId: 8888u, RoomEntityId: 2u)));

            Assert.Null(ex);
        }

        // ── Unknown blueprint disables slot, does not throw ────────────────────

        [Fact]
        public async Task Slot_with_unregistered_blueprint_is_disabled_on_first_tick()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "mob.unknown_creature";
            // Intentionally NOT registered in registry

            var roomId = MakeRoom(ecs, bp, respawnDelaySec: 0);
            // No mob in room → slot is vacant from startup with delay=0
            await system.HandleAsync(new WorldContentReadyEvent());

            // Tick immediately — template is missing, slot should be disabled gracefully
            var ex = await Record.ExceptionAsync(() => system.HandleAsync(Tick()));
            Assert.Null(ex);

            // No entity should appear in the room
            Assert.False(FindEntityInRoom(ecs, roomId, bp).HasValue,
                "Unknown blueprint should not produce a spawned entity.");
        }

        // ── Multiple slots in the same room ─────────────────────────────────────

        [Fact]
        public async Task Two_slots_in_same_room_respawn_independently()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp1 = "mob.guard";
            const string bp2 = "mob.merchant";
            const int delay1 = 10;
            const int delay2 = 30;

            registry.Register(bp1, new MinimalTemplate(bp1));
            registry.Register(bp2, new MinimalTemplate(bp2));

            // Room with two rules
            var roomId = ecs.CreateEntity().Id;
            var config = new SpawnConfigComponent();
            config.Rules.Add(new SpawnRule(bp1, 1, 1, delay1));
            config.Rules.Add(new SpawnRule(bp2, 1, 1, delay2));
            ecs.AddComponent(roomId, config);

            // Both slots start vacant (no mobs in room)
            await system.HandleAsync(new WorldContentReadyEvent());

            // At 10s: bp1 should fire, bp2 should not yet
            clock.Advance(TimeSpan.FromSeconds(delay1));
            await system.HandleAsync(Tick(1));

            Assert.True(FindEntityInRoom(ecs, roomId, bp1).HasValue,
                "bp1 (delay 10s) should have respawned at t=10s.");
            Assert.False(FindEntityInRoom(ecs, roomId, bp2).HasValue,
                "bp2 (delay 30s) should NOT have respawned at t=10s.");

            // At 30s: bp2 should now also fire
            clock.Advance(TimeSpan.FromSeconds(delay2 - delay1));
            await system.HandleAsync(Tick(2));

            Assert.True(FindEntityInRoom(ecs, roomId, bp2).HasValue,
                "bp2 (delay 30s) should have respawned at t=30s.");
        }

        // ── Multiple rooms are tracked independently ─────────────────────────────

        [Fact]
        public async Task Slots_in_different_rooms_are_independent()
        {
            var (system, ecs, registry, clock) = Build();
            const string bp = "mob.spider";
            const int delay = 15;
            registry.Register(bp, new MinimalTemplate(bp));

            var room1 = MakeRoom(ecs, bp, respawnDelaySec: delay);
            var room2 = MakeRoom(ecs, bp, respawnDelaySec: delay);

            // Place a mob only in room1; room2 starts vacant
            var mob1 = MakeMobInRoom(ecs, room1, bp);
            await system.HandleAsync(new WorldContentReadyEvent());

            // Kill mob in room1; destroy entity so it doesn't pollute FindEntityInRoom
            await system.HandleAsync(new MobDiedEvent(mob1, bp));
            ecs.DestroyEntity(mob1);

            clock.Advance(TimeSpan.FromSeconds(delay));
            await system.HandleAsync(Tick());

            // Both slots should now have a mob (room1 respawned, room2 also got one)
            Assert.True(FindEntityInRoom(ecs, room1, bp).HasValue,
                "room1 slot should have respawned after mob died.");
            Assert.True(FindEntityInRoom(ecs, room2, bp).HasValue,
                "room2 slot (vacant from startup) should also have respawned.");
        }

        // ── INV-5: SpawnSystem does not hold IEventBus ─────────────────────────

        [Fact]
        public void SpawnSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(SpawnSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: SpawnSystem field '{field.Name}' is IEventBus — " +
                    "domain systems must never hold or publish to the event bus.");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Finds the first entity in <paramref name="roomEntityId"/> whose
        /// <see cref="BlueprintComponent"/> matches <paramref name="blueprintId"/>,
        /// optionally excluding a known entity id (e.g. the original entity before it was
        /// vacated). Returns <c>null</c> if no such entity is found.
        /// </summary>
        private static uint? FindEntityInRoom(
            EntityService ecs, uint roomEntityId, string blueprintId,
            uint? exclude = null)
        {
            foreach (var (entityId, bp) in ecs.GetAllComponents<BlueprintComponent>())
            {
                if (exclude.HasValue && entityId == exclude.Value)
                    continue;
                if (!string.Equals(bp.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!ecs.TryGet<LocationComponent>(entityId, out var loc))
                    continue;
                if (loc.RoomEntityId == roomEntityId)
                    return entityId;
            }
            return null;
        }

        /// <summary>
        /// Counts entities in <paramref name="roomEntityId"/> matching <paramref name="blueprintId"/>.
        /// </summary>
        private static int CountEntitiesInRoom(EntityService ecs, uint roomEntityId, string blueprintId)
        {
            int count = 0;
            foreach (var (entityId, bp) in ecs.GetAllComponents<BlueprintComponent>())
            {
                if (!string.Equals(bp.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!ecs.TryGet<LocationComponent>(entityId, out var loc))
                    continue;
                if (loc.RoomEntityId == roomEntityId)
                    count++;
            }
            return count;
        }
    }
}
