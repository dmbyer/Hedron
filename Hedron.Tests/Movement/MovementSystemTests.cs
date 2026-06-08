using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Movement.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Movement
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="MovementSystem"/>.
    ///
    /// Coverage contract: postconditions of MovementSystem.TryMove as documented
    /// in docs/roadmap/completed/phase-2-mvp.md and the system's inline contract
    /// (IMovementSystem.cs).
    /// </summary>
    public sealed class MovementSystemTests
    {
        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a fresh <see cref="MovementSystem"/> backed by a new <see cref="EntityService"/>.
        /// </summary>
        private static (MovementSystem system, EntityService ecs) Build()
        {
            var ecs = new EntityService();
            return (new MovementSystem(ecs), ecs);
        }

        /// <summary>
        /// Creates a room entity with the given exits and optionally a blueprint id.
        /// </summary>
        private static uint MakeRoom(
            EntityService ecs,
            string? blueprintId = null,
            System.Collections.Generic.Dictionary<Direction, uint>? exits = null)
        {
            var room = ecs.CreateEntity();
            var roomComp = new RoomComponent
            {
                Name = "A room",
                Description = "A plain room.",
            };
            if (exits != null)
                foreach (var (dir, dest) in exits)
                    roomComp.Exits[dir] = dest;

            ecs.AddComponent(room.Id, roomComp);

            if (blueprintId != null)
                ecs.AddComponent(room.Id, new BlueprintComponent { BlueprintId = blueprintId });

            return room.Id;
        }

        /// <summary>
        /// Creates a player entity placed in the given room.
        /// </summary>
        private static uint MakePlayerInRoom(EntityService ecs, uint roomEntityId)
            => new EntityBuilder(ecs).AsPlayer().InRoom(roomEntityId).Build();

        // ── Happy path: successful movement ─────────────────────────────────────

        [Fact]
        public void TryMove_success_updates_LocationComponent_RoomEntityId()
        {
            var (sys, ecs) = Build();
            var destRoomId = MakeRoom(ecs);
            var fromRoomId = MakeRoom(ecs, exits: new() { [Direction.North] = destRoomId });
            var playerId = MakePlayerInRoom(ecs, fromRoomId);

            var result = sys.TryMove(playerId, Direction.North);

            Assert.True(result.Success);
            var loc = ecs.Get<LocationComponent>(playerId);
            Assert.Equal(destRoomId, loc.RoomEntityId);
        }

        [Fact]
        public void TryMove_success_returns_correct_FromRoomEntityId()
        {
            var (sys, ecs) = Build();
            var destRoomId = MakeRoom(ecs);
            var fromRoomId = MakeRoom(ecs, exits: new() { [Direction.South] = destRoomId });
            var playerId = MakePlayerInRoom(ecs, fromRoomId);

            var result = sys.TryMove(playerId, Direction.South);

            Assert.True(result.Success);
            Assert.Equal(fromRoomId, result.FromRoomEntityId);
        }

        [Fact]
        public void TryMove_success_returns_correct_ToRoomEntityId()
        {
            var (sys, ecs) = Build();
            var destRoomId = MakeRoom(ecs);
            var fromRoomId = MakeRoom(ecs, exits: new() { [Direction.East] = destRoomId });
            var playerId = MakePlayerInRoom(ecs, fromRoomId);

            var result = sys.TryMove(playerId, Direction.East);

            Assert.True(result.Success);
            Assert.Equal(destRoomId, result.ToRoomEntityId);
        }

        [Fact]
        public void TryMove_success_sets_RoomBlueprintId_from_destination_BlueprintComponent()
        {
            var (sys, ecs) = Build();
            var destRoomId = MakeRoom(ecs, blueprintId: "room.north.001");
            var fromRoomId = MakeRoom(ecs, exits: new() { [Direction.North] = destRoomId });
            var playerId = MakePlayerInRoom(ecs, fromRoomId);

            var result = sys.TryMove(playerId, Direction.North);

            Assert.True(result.Success);
            var loc = ecs.Get<LocationComponent>(playerId);
            Assert.Equal("room.north.001", loc.RoomBlueprintId);
        }

        [Fact]
        public void TryMove_success_sets_null_RoomBlueprintId_when_destination_has_no_BlueprintComponent()
        {
            var (sys, ecs) = Build();
            // Destination room has no BlueprintComponent.
            var destRoomId = MakeRoom(ecs, blueprintId: null);
            var fromRoomId = MakeRoom(ecs, exits: new() { [Direction.West] = destRoomId });
            var playerId = MakePlayerInRoom(ecs, fromRoomId);

            var result = sys.TryMove(playerId, Direction.West);

            Assert.True(result.Success);
            var loc = ecs.Get<LocationComponent>(playerId);
            Assert.Null(loc.RoomBlueprintId);
        }

        [Fact]
        public void TryMove_success_allows_all_six_directions()
        {
            var directions = new[]
            {
                Direction.North,
                Direction.South,
                Direction.East,
                Direction.West,
                Direction.Up,
                Direction.Down,
            };

            foreach (var dir in directions)
            {
                var (sys, ecs) = Build();
                var destRoomId = MakeRoom(ecs);
                var fromRoomId = MakeRoom(ecs, exits: new() { [dir] = destRoomId });
                var playerId = MakePlayerInRoom(ecs, fromRoomId);

                var result = sys.TryMove(playerId, dir);

                Assert.True(result.Success, $"Expected TryMove to succeed for direction {dir}");
                Assert.Equal(destRoomId, ecs.Get<LocationComponent>(playerId).RoomEntityId);
            }
        }

        // ── Exit resolution ──────────────────────────────────────────────────────

        [Fact]
        public void TryMove_uses_exit_map_not_hardcoded_adjacency()
        {
            // Sanity: east exit leads to the correct room even if there are many exits.
            var (sys, ecs) = Build();
            var roomNorth = MakeRoom(ecs);
            var roomEast = MakeRoom(ecs);
            var fromRoomId = MakeRoom(ecs, exits: new()
            {
                [Direction.North] = roomNorth,
                [Direction.East]  = roomEast,
            });
            var playerId = MakePlayerInRoom(ecs, fromRoomId);

            var result = sys.TryMove(playerId, Direction.East);

            Assert.True(result.Success);
            Assert.Equal(roomEast, result.ToRoomEntityId);
        }

        [Fact]
        public void TryMove_blocked_when_direction_not_in_exit_map()
        {
            // Room has a North exit but player tries to go South.
            var (sys, ecs) = Build();
            var roomNorth = MakeRoom(ecs);
            var fromRoomId = MakeRoom(ecs, exits: new() { [Direction.North] = roomNorth });
            var playerId = MakePlayerInRoom(ecs, fromRoomId);

            var result = sys.TryMove(playerId, Direction.South);

            Assert.False(result.Success);
        }

        [Fact]
        public void TryMove_blocked_when_room_has_no_exits_at_all()
        {
            var (sys, ecs) = Build();
            var fromRoomId = MakeRoom(ecs); // no exits
            var playerId = MakePlayerInRoom(ecs, fromRoomId);

            var result = sys.TryMove(playerId, Direction.North);

            Assert.False(result.Success);
        }

        // ── Validation: missing components ───────────────────────────────────────

        [Fact]
        public void TryMove_blocked_when_entity_has_no_LocationComponent()
        {
            var (sys, ecs) = Build();
            // Create a player with no LocationComponent.
            var player = ecs.CreateEntity();
            ecs.AddComponent(player.Id, new CharacterComponent());

            var result = sys.TryMove(player.Id, Direction.North);

            Assert.False(result.Success);
        }

        [Fact]
        public void TryMove_blocked_when_current_room_entity_has_no_RoomComponent()
        {
            // Player has a LocationComponent pointing at a bare entity (no RoomComponent).
            var (sys, ecs) = Build();
            var bareEntity = ecs.CreateEntity(); // no RoomComponent
            var playerId = MakePlayerInRoom(ecs, bareEntity.Id);

            var result = sys.TryMove(playerId, Direction.North);

            Assert.False(result.Success);
        }

        [Fact]
        public void TryMove_blocked_when_current_room_entity_id_is_zero()
        {
            // LocationComponent default RoomEntityId is 0; no room should exist at id 0.
            var (sys, ecs) = Build();
            var player = ecs.CreateEntity();
            ecs.AddComponent(player.Id, new CharacterComponent());
            ecs.AddComponent(player.Id, new LocationComponent { RoomEntityId = 0 });

            var result = sys.TryMove(player.Id, Direction.North);

            Assert.False(result.Success);
        }

        // ── LocationComponent mutation idempotency ───────────────────────────────

        [Fact]
        public void TryMove_blocked_does_not_mutate_LocationComponent()
        {
            var (sys, ecs) = Build();
            var fromRoomId = MakeRoom(ecs); // no exits
            var playerId = MakePlayerInRoom(ecs, fromRoomId);
            var originalRoomId = ecs.Get<LocationComponent>(playerId).RoomEntityId;

            var result = sys.TryMove(playerId, Direction.North);

            Assert.False(result.Success);
            Assert.Equal(originalRoomId, ecs.Get<LocationComponent>(playerId).RoomEntityId);
        }

        [Fact]
        public void TryMove_successive_moves_chain_correctly()
        {
            // A → B → C in two calls.
            var (sys, ecs) = Build();
            var roomC = MakeRoom(ecs);
            var roomB = MakeRoom(ecs, exits: new() { [Direction.North] = roomC });
            var roomA = MakeRoom(ecs, exits: new() { [Direction.North] = roomB });
            var playerId = MakePlayerInRoom(ecs, roomA);

            sys.TryMove(playerId, Direction.North); // A → B
            var secondResult = sys.TryMove(playerId, Direction.North); // B → C

            Assert.True(secondResult.Success);
            Assert.Equal(roomC, ecs.Get<LocationComponent>(playerId).RoomEntityId);
        }

        // ── INV-5: MovementSystem does not hold IEventBus ────────────────────────

        [Fact]
        public void MovementSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(MovementSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: MovementSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }
    }
}
