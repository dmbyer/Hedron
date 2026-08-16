using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Broadcast
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="BroadcastSystem"/> audience routing.
    ///
    /// Coverage contract: postconditions of docs/use-cases/output-framework.md (broadcast section):
    ///   - SendToRoomAsync: delivers to every PlayerComponent entity in the room with a session.
    ///   - SendToRoomAsync: skips entities in other rooms.
    ///   - SendToRoomAsync: skips entities that have no PlayerComponent (items, mobs).
    ///   - SendToRoomAsync: skips entities that have no registered session.
    ///   - SendToRoomAsync + audienceFilter null: all players in room receive the message.
    ///   - SendToRoomAsync + audienceFilter excludes sender: sender receives nothing, others receive.
    ///   - SendToRoomAsync + audienceFilter Self: only the specified entity receives.
    ///   - SendToAllAsync: delivers to every registered session regardless of room.
    ///   - SendToAllAsync: reaches zero entities when no sessions registered.
    ///   - INV-5: BroadcastSystem holds no IEventBus field.
    /// </summary>
    public sealed class BroadcastSystemTests
    {
        // ── Test double: stub ISessionManager ───────────────────────────────────

        /// <summary>
        /// Hand-rolled stub <see cref="ISessionManager"/>.
        /// Maintains a simple dictionary keyed by player entity id.
        /// </summary>
        private sealed class StubSessionManager : ISessionManager
        {
            private readonly Dictionary<uint, ISession> _sessions = new();

            public void Register(ISession session) =>
                _sessions[session.PlayerEntityId] = session;

            public void Unregister(uint playerEntityId) =>
                _sessions.Remove(playerEntityId);

            public ISession? GetSession(uint playerEntityId) =>
                _sessions.TryGetValue(playerEntityId, out var s) ? s : null;

            public IReadOnlyCollection<ISession> GetAll() =>
                _sessions.Values.ToList();
        }

        // ── Factory helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// A minimal <see cref="IOutputMessage"/> used as the broadcast payload.
        /// Tests assert on message type only, never on prose text.
        /// </summary>
        private sealed class TestMessage : IOutputMessage
        {
            public OutputCategory Category => OutputCategory.Info;
        }

        /// <summary>
        /// Builds a fresh system with its three dependencies.
        /// </summary>
        private static (BroadcastSystem system, EntityService ecs,
            StubSessionManager sessions, RecordingOutput output) Build()
        {
            var ecs = new EntityService();
            var sessions = new StubSessionManager();
            var output = new RecordingOutput();
            var system = new BroadcastSystem(ecs, sessions, output);
            return (system, ecs, sessions, output);
        }

        /// <summary>
        /// Creates a player entity in the given room and registers a session for it.
        /// Returns the entity id.
        /// </summary>
        private static uint MakeConnectedPlayer(
            EntityService ecs,
            StubSessionManager sessions,
            uint roomEntityId,
            string displayName = "Player")
        {
            var entityId = new EntityBuilder(ecs)
                .AsPlayer()
                .InRoom(roomEntityId)
                .With(new PlayerComponent { DisplayName = displayName })
                .Build();

            sessions.Register(new StubSession(entityId));
            return entityId;
        }

        /// <summary>Creates a bare room entity and returns its id.</summary>
        private static uint MakeRoom(EntityService ecs)
        {
            var room = ecs.CreateEntity();
            ecs.AddComponent(room.Id, new RoomComponent { Name = "Test Room", Description = "A room." });
            return room.Id;
        }

        // ── SendToRoomAsync — basic delivery ─────────────────────────────────────

        [Fact]
        public async Task SendToRoomAsync_delivers_to_single_player_in_room()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);
            var playerId = MakeConnectedPlayer(ecs, sessions, roomId);
            var msg = new TestMessage();

            await sys.SendToRoomAsync(roomId, msg);

            output.AssertMessage<TestMessage>(playerId);
        }

        [Fact]
        public async Task SendToRoomAsync_delivers_to_all_players_in_room()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);
            var player1 = MakeConnectedPlayer(ecs, sessions, roomId, "Alice");
            var player2 = MakeConnectedPlayer(ecs, sessions, roomId, "Bob");
            var player3 = MakeConnectedPlayer(ecs, sessions, roomId, "Carol");
            var msg = new TestMessage();

            await sys.SendToRoomAsync(roomId, msg);

            output.AssertMessage<TestMessage>(player1);
            output.AssertMessage<TestMessage>(player2);
            output.AssertMessage<TestMessage>(player3);
        }

        [Fact]
        public async Task SendToRoomAsync_exact_recipient_count_matches_players_in_room()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);
            MakeConnectedPlayer(ecs, sessions, roomId, "Alice");
            MakeConnectedPlayer(ecs, sessions, roomId, "Bob");

            await sys.SendToRoomAsync(roomId, new TestMessage());

            // Exactly two deliveries — no extras.
            Assert.Equal(2, output.All.Count(r => r.MessageType == typeof(TestMessage)));
        }

        // ── SendToRoomAsync — negative: wrong room ────────────────────────────────

        [Fact]
        public async Task SendToRoomAsync_does_not_deliver_to_player_in_different_room()
        {
            var (sys, ecs, sessions, output) = Build();
            var targetRoom = MakeRoom(ecs);
            var otherRoom = MakeRoom(ecs);
            var playerInTarget = MakeConnectedPlayer(ecs, sessions, targetRoom);
            var playerElsewhere = MakeConnectedPlayer(ecs, sessions, otherRoom, "Elsewhere");

            await sys.SendToRoomAsync(targetRoom, new TestMessage());

            output.AssertMessage<TestMessage>(playerInTarget);
            Assert.False(output.HasMessage<TestMessage>(playerElsewhere),
                "Player in another room must not receive a room-scoped broadcast.");
        }

        // ── SendToRoomAsync — negative: no PlayerComponent ────────────────────────

        [Fact]
        public async Task SendToRoomAsync_skips_entity_without_PlayerComponent()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);

            // A mob entity in the room — no PlayerComponent, but has a session registered
            // (edge case: should still be skipped because HasComponent<PlayerComponent> is false).
            var mob = new EntityBuilder(ecs).AsMob("goblin").InRoom(roomId).Build();
            sessions.Register(new StubSession(mob));

            await sys.SendToRoomAsync(roomId, new TestMessage());

            Assert.Empty(output.All);
        }

        // ── SendToRoomAsync — negative: no registered session ─────────────────────

        [Fact]
        public async Task SendToRoomAsync_skips_player_with_no_session()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);

            // Player entity exists in the room but has no registered session.
            new EntityBuilder(ecs)
                .AsPlayer()
                .InRoom(roomId)
                .With(new PlayerComponent { DisplayName = "Ghost" })
                .Build();

            await sys.SendToRoomAsync(roomId, new TestMessage());

            Assert.Empty(output.All);
        }

        // ── SendToRoomAsync — audienceFilter: sender exclusion ────────────────────

        [Fact]
        public async Task SendToRoomAsync_excludeSender_filter_delivers_to_others_not_sender()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);
            var sender = MakeConnectedPlayer(ecs, sessions, roomId, "Sender");
            var observer1 = MakeConnectedPlayer(ecs, sessions, roomId, "Observer1");
            var observer2 = MakeConnectedPlayer(ecs, sessions, roomId, "Observer2");

            // Audience filter: "Others" — everyone except the sender.
            await sys.SendToRoomAsync(roomId, new TestMessage(), id => id != sender);

            Assert.False(output.HasMessage<TestMessage>(sender),
                "Sender must be excluded when audienceFilter excludes its id.");
            output.AssertMessage<TestMessage>(observer1);
            output.AssertMessage<TestMessage>(observer2);
        }

        [Fact]
        public async Task SendToRoomAsync_excludeSender_filter_total_recipients_is_others_count()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);
            var sender = MakeConnectedPlayer(ecs, sessions, roomId, "Sender");
            MakeConnectedPlayer(ecs, sessions, roomId, "Observer1");
            MakeConnectedPlayer(ecs, sessions, roomId, "Observer2");

            await sys.SendToRoomAsync(roomId, new TestMessage(), id => id != sender);

            // 3 players total; sender excluded → exactly 2 deliveries.
            Assert.Equal(2, output.All.Count(r => r.MessageType == typeof(TestMessage)));
        }

        // ── SendToRoomAsync — audienceFilter: Self (only sender) ──────────────────

        [Fact]
        public async Task SendToRoomAsync_selfOnly_filter_delivers_only_to_self()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);
            var self = MakeConnectedPlayer(ecs, sessions, roomId, "Self");
            var other = MakeConnectedPlayer(ecs, sessions, roomId, "Other");

            // Audience filter: "Self" — only the self entity.
            await sys.SendToRoomAsync(roomId, new TestMessage(), id => id == self);

            output.AssertMessage<TestMessage>(self);
            Assert.False(output.HasMessage<TestMessage>(other),
                "Other player must not receive when audienceFilter selects only self.");
        }

        // ── SendToRoomAsync — audienceFilter: Everyone (null) ─────────────────────

        [Fact]
        public async Task SendToRoomAsync_null_filter_delivers_to_all_players_in_room()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);
            var p1 = MakeConnectedPlayer(ecs, sessions, roomId, "P1");
            var p2 = MakeConnectedPlayer(ecs, sessions, roomId, "P2");

            // Null filter = Everyone audience.
            await sys.SendToRoomAsync(roomId, new TestMessage(), audienceFilter: null);

            output.AssertMessage<TestMessage>(p1);
            output.AssertMessage<TestMessage>(p2);
        }

        // ── SendToRoomAsync — audienceFilter: rejectAll ───────────────────────────

        [Fact]
        public async Task SendToRoomAsync_rejectAll_filter_delivers_to_nobody()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);
            MakeConnectedPlayer(ecs, sessions, roomId, "P1");
            MakeConnectedPlayer(ecs, sessions, roomId, "P2");

            await sys.SendToRoomAsync(roomId, new TestMessage(), _ => false);

            Assert.Empty(output.All);
        }

        // ── SendToRoomAsync — empty room ──────────────────────────────────────────

        [Fact]
        public async Task SendToRoomAsync_empty_room_produces_no_output()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);

            await sys.SendToRoomAsync(roomId, new TestMessage());

            Assert.Empty(output.All);
        }

        // ── SendToEntityAsync ────────────────────────────────────────────────────

        [Fact]
        public async Task SendToEntityAsync_delivers_to_that_player_only()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);
            var recipient = MakeConnectedPlayer(ecs, sessions, roomId, "Alice");
            var bystander = MakeConnectedPlayer(ecs, sessions, roomId, "Bob");

            await sys.SendToEntityAsync(recipient, new TestMessage());

            output.AssertMessage<TestMessage>(recipient);
            Assert.DoesNotContain(output.All, r => r.RecipientEntityId == bystander);
        }

        [Fact]
        public async Task SendToEntityAsync_with_no_session_is_a_silent_no_op()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);
            var offline = new EntityBuilder(ecs).AsPlayer().InRoom(roomId).Build(); // never registered

            await sys.SendToEntityAsync(offline, new TestMessage());

            Assert.Empty(output.All);
        }

        [Fact]
        public async Task SendToEntityAsync_does_not_require_a_LocationComponent()
        {
            // The whole point of this method over the SendToRoomAsync-with-predicate workaround:
            // a recipient with no location still receives the message instead of silently losing it.
            var (sys, ecs, sessions, output) = Build();
            var locationless = new EntityBuilder(ecs)
                .AsPlayer()
                .With(new PlayerComponent { DisplayName = "Ghost" })
                .Build();
            sessions.Register(new StubSession(locationless));

            await sys.SendToEntityAsync(locationless, new TestMessage());

            output.AssertMessage<TestMessage>(locationless);
        }

        // ── SendToAllAsync ────────────────────────────────────────────────────────

        [Fact]
        public async Task SendToAllAsync_delivers_to_every_registered_session()
        {
            var (sys, ecs, sessions, output) = Build();
            var room1 = MakeRoom(ecs);
            var room2 = MakeRoom(ecs);
            var p1 = MakeConnectedPlayer(ecs, sessions, room1, "P1");
            var p2 = MakeConnectedPlayer(ecs, sessions, room2, "P2");
            var p3 = MakeConnectedPlayer(ecs, sessions, room1, "P3");

            await sys.SendToAllAsync(new TestMessage());

            output.AssertMessage<TestMessage>(p1);
            output.AssertMessage<TestMessage>(p2);
            output.AssertMessage<TestMessage>(p3);
        }

        [Fact]
        public async Task SendToAllAsync_delivers_across_different_rooms()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomA = MakeRoom(ecs);
            var roomB = MakeRoom(ecs);
            var pA = MakeConnectedPlayer(ecs, sessions, roomA, "PA");
            var pB = MakeConnectedPlayer(ecs, sessions, roomB, "PB");

            await sys.SendToAllAsync(new TestMessage());

            // Both players in different rooms receive the system-wide message.
            output.AssertMessage<TestMessage>(pA);
            output.AssertMessage<TestMessage>(pB);
        }

        [Fact]
        public async Task SendToAllAsync_exact_count_matches_registered_session_count()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomId = MakeRoom(ecs);
            MakeConnectedPlayer(ecs, sessions, roomId, "P1");
            MakeConnectedPlayer(ecs, sessions, roomId, "P2");
            MakeConnectedPlayer(ecs, sessions, roomId, "P3");

            await sys.SendToAllAsync(new TestMessage());

            Assert.Equal(3, output.All.Count(r => r.MessageType == typeof(TestMessage)));
        }

        [Fact]
        public async Task SendToAllAsync_with_no_sessions_produces_no_output()
        {
            var (sys, _, _, output) = Build();

            await sys.SendToAllAsync(new TestMessage());

            Assert.Empty(output.All);
        }

        // ── Cross-room isolation ──────────────────────────────────────────────────

        [Fact]
        public async Task SendToRoomAsync_multiple_rooms_isolates_each_broadcast()
        {
            var (sys, ecs, sessions, output) = Build();
            var roomA = MakeRoom(ecs);
            var roomB = MakeRoom(ecs);
            var pA = MakeConnectedPlayer(ecs, sessions, roomA, "PA");
            var pB = MakeConnectedPlayer(ecs, sessions, roomB, "PB");

            // Broadcast only to room A.
            await sys.SendToRoomAsync(roomA, new TestMessage());

            output.AssertMessage<TestMessage>(pA);
            Assert.False(output.HasMessage<TestMessage>(pB),
                "Player in room B must not receive a broadcast targeted at room A.");
        }

        // ── INV-5: no IEventBus in BroadcastSystem ────────────────────────────────

        [Fact]
        public void BroadcastSystem_does_not_hold_IEventBus_field()
        {
            // INV-5: systems never hold or publish to the event bus.
            var fields = typeof(BroadcastSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: BroadcastSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus.");
            }
        }
    }
}
