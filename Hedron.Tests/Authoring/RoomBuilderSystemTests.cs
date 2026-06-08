using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.Authoring
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="RoomBuilderSystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/world-content-loading-and-admin-substrate.md
    /// and the builder interface <see cref="IRoomBuilderSystem"/>.
    ///
    /// All tests use the real <see cref="EntityService"/> and <see cref="TemplateRegistry"/>
    /// (no mocking framework).
    /// </summary>
    public sealed class RoomBuilderSystemTests
    {
        // ── Harness ──────────────────────────────────────────────────────────────

        private static (RoomBuilderSystem system, EntityService ecs, TemplateRegistry registry) Build()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var system = new RoomBuilderSystem(ecs, registry, NullLogger<RoomBuilderSystem>.Instance);
            return (system, ecs, registry);
        }

        // ── CreateRoom ───────────────────────────────────────────────────────────

        [Fact]
        public void CreateRoom_returns_nonzero_RoomEntityId()
        {
            var (sys, _, _) = Build();
            var result = sys.CreateRoom("The Library");
            Assert.NotEqual(0u, result.RoomEntityId);
        }

        [Fact]
        public void CreateRoom_returns_nonempty_BlueprintId()
        {
            var (sys, _, _) = Build();
            var result = sys.CreateRoom("The Library");
            Assert.False(string.IsNullOrWhiteSpace(result.BlueprintId));
        }

        [Fact]
        public void CreateRoom_attaches_RoomComponent_with_correct_name()
        {
            var (sys, ecs, _) = Build();
            var result = sys.CreateRoom("The Tavern");

            var room = ecs.Get<RoomComponent>(result.RoomEntityId);
            Assert.Equal("The Tavern", room.Name);
        }

        [Fact]
        public void CreateRoom_attaches_RoomComponent_with_correct_description()
        {
            var (sys, ecs, _) = Build();
            var result = sys.CreateRoom("The Cavern", "A dark, damp cave.");

            var room = ecs.Get<RoomComponent>(result.RoomEntityId);
            Assert.Equal("A dark, damp cave.", room.Description);
        }

        [Fact]
        public void CreateRoom_attaches_RoomComponent_with_empty_description_by_default()
        {
            var (sys, ecs, _) = Build();
            var result = sys.CreateRoom("The Clearing");

            var room = ecs.Get<RoomComponent>(result.RoomEntityId);
            Assert.Equal(string.Empty, room.Description);
        }

        [Fact]
        public void CreateRoom_attaches_BlueprintComponent_with_matching_id()
        {
            var (sys, ecs, _) = Build();
            var result = sys.CreateRoom("The Hall");

            var bp = ecs.Get<BlueprintComponent>(result.RoomEntityId);
            Assert.Equal(result.BlueprintId, bp.BlueprintId);
        }

        [Fact]
        public void CreateRoom_registers_template_in_registry()
        {
            var (sys, _, registry) = Build();
            var result = sys.CreateRoom("The Dungeon");

            var found = registry.TryGet(result.BlueprintId, out var template);
            Assert.True(found);
            Assert.NotNull(template);
        }

        [Fact]
        public void CreateRoom_template_has_correct_name()
        {
            var (sys, _, registry) = Build();
            var result = sys.CreateRoom("The Forge");

            registry.TryGet(result.BlueprintId, out var template);
            var roomTemplate = Assert.IsType<RoomTemplate>(template);
            Assert.Equal("The Forge", roomTemplate.Name);
        }

        [Fact]
        public void CreateRoom_template_has_correct_description()
        {
            var (sys, _, registry) = Build();
            var result = sys.CreateRoom("The Crypt", "Ancient bones litter the floor.");

            registry.TryGet(result.BlueprintId, out var template);
            var roomTemplate = Assert.IsType<RoomTemplate>(template);
            Assert.Equal("Ancient bones litter the floor.", roomTemplate.Description);
        }

        [Fact]
        public void CreateRoom_assigns_unique_blueprint_ids_to_successive_rooms()
        {
            var (sys, _, _) = Build();
            var r1 = sys.CreateRoom("Room A");
            var r2 = sys.CreateRoom("Room B");

            Assert.NotEqual(r1.BlueprintId, r2.BlueprintId);
        }

        [Fact]
        public void CreateRoom_assigns_unique_entity_ids_to_successive_rooms()
        {
            var (sys, _, _) = Build();
            var r1 = sys.CreateRoom("Room A");
            var r2 = sys.CreateRoom("Room B");

            Assert.NotEqual(r1.RoomEntityId, r2.RoomEntityId);
        }

        // ── SetRoomName ──────────────────────────────────────────────────────────

        [Fact]
        public void SetRoomName_updates_RoomComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var result = sys.CreateRoom("Old Name");

            sys.SetRoomName(result.RoomEntityId, "New Name");

            var room = ecs.Get<RoomComponent>(result.RoomEntityId);
            Assert.Equal("New Name", room.Name);
        }

        [Fact]
        public void SetRoomName_updates_template_in_registry()
        {
            var (sys, _, registry) = Build();
            var result = sys.CreateRoom("Old Name");

            sys.SetRoomName(result.RoomEntityId, "New Name");

            registry.TryGet(result.BlueprintId, out var template);
            var roomTemplate = Assert.IsType<RoomTemplate>(template);
            Assert.Equal("New Name", roomTemplate.Name);
        }

        [Fact]
        public void SetRoomName_is_noop_for_unknown_entity()
        {
            // Calling SetRoomName on a non-existent entity should not throw.
            var (sys, _, _) = Build();
            sys.SetRoomName(99999u, "Ghost Room");
        }

        // ── SetRoomDescription ───────────────────────────────────────────────────

        [Fact]
        public void SetRoomDescription_updates_RoomComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var result = sys.CreateRoom("The Market");

            sys.SetRoomDescription(result.RoomEntityId, "Stalls packed with wares.");

            var room = ecs.Get<RoomComponent>(result.RoomEntityId);
            Assert.Equal("Stalls packed with wares.", room.Description);
        }

        [Fact]
        public void SetRoomDescription_updates_template_in_registry()
        {
            var (sys, _, registry) = Build();
            var result = sys.CreateRoom("The Market");

            sys.SetRoomDescription(result.RoomEntityId, "Stalls packed with wares.");

            registry.TryGet(result.BlueprintId, out var template);
            var roomTemplate = Assert.IsType<RoomTemplate>(template);
            Assert.Equal("Stalls packed with wares.", roomTemplate.Description);
        }

        [Fact]
        public void SetRoomDescription_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetRoomDescription(99999u, "Nowhere.");
        }

        // ── LinkExits ────────────────────────────────────────────────────────────

        [Fact]
        public void LinkExits_sets_exit_on_source_RoomComponent()
        {
            var (sys, ecs, _) = Build();
            var source = sys.CreateRoom("North Room");
            var target = sys.CreateRoom("South Room");

            sys.LinkExits(source.RoomEntityId, Direction.South, target.RoomEntityId, bidirectional: false);

            var sourceRoom = ecs.Get<RoomComponent>(source.RoomEntityId);
            Assert.True(sourceRoom.Exits.ContainsKey(Direction.South));
            Assert.Equal(target.RoomEntityId, sourceRoom.Exits[Direction.South]);
        }

        [Fact]
        public void LinkExits_bidirectional_sets_reverse_exit_on_target()
        {
            var (sys, ecs, _) = Build();
            var source = sys.CreateRoom("North Room");
            var target = sys.CreateRoom("South Room");

            sys.LinkExits(source.RoomEntityId, Direction.South, target.RoomEntityId, bidirectional: true);

            var targetRoom = ecs.Get<RoomComponent>(target.RoomEntityId);
            Assert.True(targetRoom.Exits.ContainsKey(Direction.North));
            Assert.Equal(source.RoomEntityId, targetRoom.Exits[Direction.North]);
        }

        [Fact]
        public void LinkExits_unidirectional_does_not_set_reverse_exit()
        {
            var (sys, ecs, _) = Build();
            var source = sys.CreateRoom("East Room");
            var target = sys.CreateRoom("West Room");

            sys.LinkExits(source.RoomEntityId, Direction.East, target.RoomEntityId, bidirectional: false);

            var targetRoom = ecs.Get<RoomComponent>(target.RoomEntityId);
            Assert.False(targetRoom.Exits.ContainsKey(Direction.West));
        }

        [Fact]
        public void LinkExits_mirrors_exit_to_source_template()
        {
            var (sys, _, registry) = Build();
            var source = sys.CreateRoom("Upper Chamber");
            var target = sys.CreateRoom("Lower Chamber");

            sys.LinkExits(source.RoomEntityId, Direction.Down, target.RoomEntityId, bidirectional: false);

            registry.TryGet(source.BlueprintId, out var template);
            var sourceTemplate = Assert.IsType<RoomTemplate>(template);
            Assert.True(sourceTemplate.Exits.ContainsKey(Direction.Down));
            Assert.Equal(target.BlueprintId, sourceTemplate.Exits[Direction.Down]);
        }

        [Fact]
        public void LinkExits_bidirectional_mirrors_reverse_exit_to_target_template()
        {
            var (sys, _, registry) = Build();
            var source = sys.CreateRoom("Upper Chamber");
            var target = sys.CreateRoom("Lower Chamber");

            sys.LinkExits(source.RoomEntityId, Direction.Down, target.RoomEntityId, bidirectional: true);

            registry.TryGet(target.BlueprintId, out var template);
            var targetTemplate = Assert.IsType<RoomTemplate>(template);
            Assert.True(targetTemplate.Exits.ContainsKey(Direction.Up));
            Assert.Equal(source.BlueprintId, targetTemplate.Exits[Direction.Up]);
        }

        [Fact]
        public void LinkExits_is_noop_for_unknown_source_entity()
        {
            // Should not throw even when source entity doesn't exist.
            var (sys, ecs, _) = Build();
            var target = sys.CreateRoom("Target");

            sys.LinkExits(99999u, Direction.North, target.RoomEntityId, bidirectional: false);
        }

        // ── INV-5: RoomBuilderSystem does not hold IEventBus ─────────────────────

        [Fact]
        public void RoomBuilderSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(RoomBuilderSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: RoomBuilderSystem field '{field.Name}' is IEventBus — " +
                    "domain systems must never hold or publish to the event bus.");
            }
        }
    }
}
