using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="AreaSystem"/>.
    ///
    /// All tests use the real <see cref="EntityService"/> and <see cref="TemplateRegistry"/>
    /// (no mocking framework).
    /// </summary>
    public sealed class AreaSystemTests
    {
        // ── Harness ──────────────────────────────────────────────────────────────

        private static (AreaSystem system, EntityService ecs, TemplateRegistry registry) Build()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var system = new AreaSystem(ecs, registry);
            return (system, ecs, registry);
        }

        /// <summary>Creates a room entity with a BlueprintComponent and registered RoomTemplate.</summary>
        private static (uint entityId, RoomTemplate template) MakeRoom(
            EntityService ecs, TemplateRegistry registry, string blueprintId, uint areaEntityId = 0)
        {
            var entity = ecs.CreateEntity();
            var room = new RoomComponent { Name = "Test Room" };
            if (areaEntityId != 0)
                room.AreaEntityId = areaEntityId;
            ecs.AddComponent(entity.Id, room);
            ecs.AddComponent(entity.Id, new BlueprintComponent { BlueprintId = blueprintId });
            var template = new RoomTemplate(blueprintId) { Name = "Test Room" };
            registry.Register(blueprintId, template);
            return (entity.Id, template);
        }

        // ── GetRoomsInArea ───────────────────────────────────────────────────────

        [Fact]
        public void AreaSystem_GetRoomsInArea_ReturnsOnlyMatchingRooms()
        {
            var (sys, ecs, registry) = Build();

            var (r1, _) = MakeRoom(ecs, registry, "room.a", areaEntityId: 42);
            var (r2, _) = MakeRoom(ecs, registry, "room.b", areaEntityId: 42);
            var (r3, _) = MakeRoom(ecs, registry, "room.c", areaEntityId: 99);

            var result = sys.GetRoomsInArea(42);

            Assert.Equal(2, result.Count);
            Assert.Contains(r1, result);
            Assert.Contains(r2, result);
            Assert.DoesNotContain(r3, result);
        }

        [Fact]
        public void AreaSystem_GetRoomsInArea_ReturnsEmpty_WhenNoRoomsInArea()
        {
            var (sys, ecs, registry) = Build();

            // Area entity exists but no rooms assigned to it.
            var areaEntity = ecs.CreateEntity();
            ecs.AddComponent(areaEntity.Id, new AreaComponent { AreaId = "area.empty", Name = "Empty Area" });

            var result = sys.GetRoomsInArea(areaEntity.Id);

            Assert.Empty(result);
        }

        // ── GetAreaForRoom ───────────────────────────────────────────────────────

        [Fact]
        public void AreaSystem_GetAreaForRoom_ReturnsMembership()
        {
            var (sys, ecs, registry) = Build();

            var (roomId, _) = MakeRoom(ecs, registry, "room.assigned", areaEntityId: 5);

            var result = sys.GetAreaForRoom(roomId);

            Assert.Equal(5u, result);
        }

        [Fact]
        public void AreaSystem_GetAreaForRoom_ReturnsNull_WhenUnassigned()
        {
            var (sys, ecs, registry) = Build();

            // Room entity with AreaEntityId = 0 (default).
            var (roomId, _) = MakeRoom(ecs, registry, "room.unassigned");

            var result = sys.GetAreaForRoom(roomId);

            Assert.Null(result);
        }

        [Fact]
        public void AreaSystem_GetAreaForRoom_ReturnsNull_ForUnknownEntity()
        {
            var (sys, _, _) = Build();

            var result = sys.GetAreaForRoom(99999u);

            Assert.Null(result);
        }

        // ── AssignRoomToArea ─────────────────────────────────────────────────────

        [Fact]
        public void AreaSystem_AssignRoomToArea_SetsAreaEntityId()
        {
            var (sys, ecs, registry) = Build();

            var (roomId, _) = MakeRoom(ecs, registry, "room.test");
            var areaEntity = ecs.CreateEntity();
            ecs.AddComponent(areaEntity.Id, new AreaComponent { AreaId = "core.area.test", Name = "Test Area" });

            sys.AssignRoomToArea(roomId, areaEntity.Id, "core.area.test");

            var room = ecs.Get<RoomComponent>(roomId);
            Assert.Equal(areaEntity.Id, room.AreaEntityId);
        }

        [Fact]
        public void AreaSystem_AssignRoomToArea_MirrorsAreaIdToTemplate()
        {
            var (sys, ecs, registry) = Build();

            var (roomId, roomTemplate) = MakeRoom(ecs, registry, "room.mirror-test");
            var areaEntity = ecs.CreateEntity();
            ecs.AddComponent(areaEntity.Id, new AreaComponent { AreaId = "core.area.test", Name = "Test Area" });

            sys.AssignRoomToArea(roomId, areaEntity.Id, "core.area.test");

            Assert.Equal("core.area.test", roomTemplate.AreaId);
        }

        // ── INV-5: AreaSystem does not hold IEventBus ────────────────────────────

        [Fact]
        public void AreaSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(AreaSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: AreaSystem field '{field.Name}' is IEventBus — " +
                    "domain systems must never hold or publish to the event bus.");
            }
        }
    }
}
