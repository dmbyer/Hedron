using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Flow-tier tests for <see cref="WorldContentLoader"/> — specifically the
    /// <c>LinkRoomAreas</c> phase introduced in WP-1 of the area-model use case.
    ///
    /// All tests use the real <see cref="EntityService"/> and <see cref="TemplateRegistry"/>.
    /// A stub <see cref="IContentSerializer"/> prevents file I/O; templates are pre-registered
    /// directly into the registry before each <see cref="WorldContentLoader.LoadAndSpawnAsync"/> call.
    ///
    /// Strategy: the content directory is set to a guaranteed-nonexistent path so
    /// <c>LoadTemplatesAsync</c> returns early without reading files. Pre-registered templates
    /// (count &gt; 0) cause <c>LoadAndSpawnAsync</c> to take the <c>else</c> branch: spawn,
    /// link exits, place items/mobs, and then call <c>LinkRoomAreas</c>.
    /// </summary>
    public sealed class WorldContentLoaderTests
    {
        // ── Stubs ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Stub content serializer — never deserializes anything (tests pre-register templates directly).
        /// </summary>
        private sealed class StubSerializer : IContentSerializer
        {
            public string FormatExtension => ".yaml";
            public IEntityTemplate Deserialize(string kind, string fileBody)
                => throw new System.NotSupportedException("StubSerializer: no file I/O in unit tests.");
        }

        /// <summary>
        /// Stub content writer — discards all writes (no file system access needed in tests).
        /// </summary>
        private sealed class StubRoomContentWriter : IRoomContentWriter
        {
            public System.Threading.Tasks.Task WriteAsync(RoomTemplate template, System.Threading.CancellationToken ct = default)
                => System.Threading.Tasks.Task.CompletedTask;
        }

        // ── Harness ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="WorldContentLoader"/> pointing at a nonexistent content directory.
        /// Templates are pre-registered into the shared <paramref name="registry"/> before calling
        /// <see cref="WorldContentLoader.LoadAndSpawnAsync"/>.
        /// </summary>
        private static (WorldContentLoader loader, EntityService ecs, TemplateRegistry registry) Build()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var worldConfig = new WorldConfiguration();
            var options = Options.Create(new WorldOptions
            {
                ContentDirectory = @"Z:\nonexistent-test-dir-that-never-exists",
                StartingRoomBlueprintId = "room.void",
            });
            var loader = new WorldContentLoader(
                ecs,
                registry,
                new StubSerializer(),
                new StubRoomContentWriter(),
                worldConfig,
                options,
                NullLogger<WorldContentLoader>.Instance);
            return (loader, ecs, registry);
        }

        // ── Helper: find the entity whose BlueprintComponent matches the given id ──

        private static uint? FindEntityByBlueprint(EntityService ecs, string blueprintId)
        {
            foreach (var (entityId, bp) in ecs.GetAllComponents<BlueprintComponent>())
            {
                if (string.Equals(bp.BlueprintId, blueprintId, System.StringComparison.OrdinalIgnoreCase))
                    return entityId;
            }
            return null;
        }

        // ── LoadAndSpawnAsync: LinkRoomAreas sets AreaEntityId ───────────────────

        [Fact]
        public async Task WorldContentLoader_LinkRoomAreas_SetsAreaEntityId()
        {
            var (loader, ecs, registry) = Build();

            const string areaBpId = "core.area.midlands";
            const string roomBpId = "core.room.town-square";

            // Register an area template and a room template that references the area.
            var areaTemplate = new AreaTemplate(areaBpId) { Name = "The Midlands" };
            registry.Register(areaBpId, areaTemplate);

            var roomTemplate = new RoomTemplate(roomBpId) { Name = "Town Square", AreaId = areaBpId };
            registry.Register(roomBpId, roomTemplate);

            // Load — spawns both, then LinkRoomAreas sets room.AreaEntityId.
            await loader.LoadAndSpawnAsync();

            var areaEntityId = FindEntityByBlueprint(ecs, areaBpId);
            var roomEntityId = FindEntityByBlueprint(ecs, roomBpId);

            Assert.True(areaEntityId.HasValue, "Area entity should have been spawned.");
            Assert.True(roomEntityId.HasValue, "Room entity should have been spawned.");

            var room = ecs.Get<RoomComponent>(roomEntityId!.Value);
            Assert.Equal(areaEntityId!.Value, room.AreaEntityId);
        }

        // ── LoadAndSpawnAsync: tolerates unknown area reference ──────────────────

        [Fact]
        public async Task WorldContentLoader_LinkRoomAreas_ToleratesMissingArea()
        {
            var (loader, ecs, registry) = Build();

            const string roomBpId = "core.room.orphan";

            // Room template references a blueprint that was never registered.
            var roomTemplate = new RoomTemplate(roomBpId)
            {
                Name = "Orphan Room",
                AreaId = "core.area.does-not-exist",
            };
            registry.Register(roomBpId, roomTemplate);

            // Should not throw even though the area blueprint is missing.
            var ex = await Record.ExceptionAsync(() => loader.LoadAndSpawnAsync());
            Assert.Null(ex);

            var roomEntityId = FindEntityByBlueprint(ecs, roomBpId);
            Assert.True(roomEntityId.HasValue, "Room entity should still be spawned.");

            var room = ecs.Get<RoomComponent>(roomEntityId!.Value);
            Assert.Equal(0u, room.AreaEntityId);
        }

        // ── LoadAndSpawnAsync: room with no AreaId is unaffected ─────────────────

        [Fact]
        public async Task WorldContentLoader_LinkRoomAreas_LeavesUnassignedRoomAtZero()
        {
            var (loader, ecs, registry) = Build();

            const string areaBpId = "core.area.forest";
            const string roomBpId = "core.room.clearing";

            registry.Register(areaBpId, new AreaTemplate(areaBpId) { Name = "The Forest" });
            // Room template has no AreaId set.
            registry.Register(roomBpId, new RoomTemplate(roomBpId) { Name = "Clearing" });

            await loader.LoadAndSpawnAsync();

            var roomEntityId = FindEntityByBlueprint(ecs, roomBpId);
            Assert.True(roomEntityId.HasValue);
            var room = ecs.Get<RoomComponent>(roomEntityId!.Value);
            Assert.Equal(0u, room.AreaEntityId);
        }

        // ── Persisted player-owned instance must not shadow authored world spawn ──

        /// <summary>
        /// Regression: an authored item that a player has picked up is restored from the DB before
        /// the world loader runs. The restored entity keeps its <see cref="BlueprintComponent"/> as an
        /// origin record (INV-21) and carries <see cref="PersistentEntity"/>. The loader must still
        /// re-spawn the authored world copy from YAML and place it in its room — the persisted,
        /// player-owned instance must not suppress world content (the blueprint/instance separation).
        /// </summary>
        [Fact]
        public async Task WorldContentLoader_PersistedInstance_DoesNotShadow_AuthoredWorldSpawn()
        {
            var (loader, ecs, registry) = Build();

            const string roomBpId = "core.room.start";
            const string itemBpId = "core.item.sword";

            // Simulate a restored-from-persistence, player-owned copy of the item: same blueprint id,
            // PersistentEntity, no LocationComponent (it lives in a player's inventory/container).
            var persisted = ecs.CreateEntity();
            ecs.AddComponent(persisted.Id, new BlueprintComponent { BlueprintId = itemBpId });
            ecs.AddComponent(persisted.Id, new ItemDataComponent { Name = "sword" });
            ecs.AddComponent(persisted.Id, new PersistentEntity());

            // Authored world content: the room and the item that spawns into it.
            registry.Register(roomBpId, new RoomTemplate(roomBpId) { Name = "Starting Room" });
            registry.Register(itemBpId, new ItemTemplate(itemBpId)
            {
                Name = "sword",
                SpawnRoomBlueprintId = roomBpId,
            });

            await loader.LoadAndSpawnAsync();

            var roomEntityId = FindEntityByBlueprint(ecs, roomBpId);
            Assert.True(roomEntityId.HasValue, "Room entity should have been spawned.");

            // A fresh world copy (distinct from the persisted instance) must exist, placed in the room,
            // and must NOT be persistent.
            uint? worldCopy = null;
            foreach (var (entityId, bp) in ecs.GetAllComponents<BlueprintComponent>())
            {
                if (entityId == persisted.Id) continue;
                if (string.Equals(bp.BlueprintId, itemBpId, System.StringComparison.OrdinalIgnoreCase))
                {
                    worldCopy = entityId;
                    break;
                }
            }

            Assert.True(worldCopy.HasValue,
                "Authored world copy should re-spawn from YAML despite the persisted player-owned instance.");
            Assert.True(ecs.HasComponent<LocationComponent>(worldCopy!.Value),
                "World copy should be placed in a room.");
            Assert.Equal(roomEntityId!.Value, ecs.Get<LocationComponent>(worldCopy.Value).RoomEntityId);
            Assert.False(ecs.HasComponent<PersistentEntity>(worldCopy.Value),
                "World copy is world content — it must not be persistent.");

            // The persisted, player-owned instance is untouched: still persistent, still no location.
            Assert.True(ecs.HasComponent<PersistentEntity>(persisted.Id));
            Assert.False(ecs.HasComponent<LocationComponent>(persisted.Id),
                "The player-owned instance must not be re-placed into the world.");
        }

        // ── ReloadAsync: rebuild tears down world content, preserves persistent entities ─

        /// <summary>
        /// <see cref="WorldContentLoader.ReloadAsync"/> is a full rebuild, not additive: it destroys
        /// every world-content entity (BlueprintComponent, no PersistentEntity) before re-spawning
        /// from YAML. Persistent entities (players, player-owned items) survive the teardown. Here the
        /// content directory is empty, so the rebuild tears the world down to a void room — proving the
        /// destroy half — while the persistent entity is left intact.
        /// </summary>
        [Fact]
        public async Task WorldContentLoader_ReloadAsync_TearsDownWorldContent_PreservesPersistent()
        {
            var (loader, ecs, registry) = Build();

            const string areaBpId = "core.area.midlands";
            const string roomBpId = "core.room.market";

            registry.Register(areaBpId, new AreaTemplate(areaBpId) { Name = "The Midlands" });
            registry.Register(roomBpId, new RoomTemplate(roomBpId) { Name = "Market", AreaId = areaBpId });

            await loader.LoadAndSpawnAsync();
            Assert.True(FindEntityByBlueprint(ecs, roomBpId).HasValue, "Room should spawn on initial load.");

            // A persistent, player-owned entity that must survive the rebuild.
            var player = ecs.CreateEntity();
            ecs.AddComponent(player.Id, new BlueprintComponent { BlueprintId = "player.test" });
            ecs.AddComponent(player.Id, new PersistentEntity());

            // ReloadAsync with an empty content directory: world content is torn down; because no
            // templates re-load, the world rebuilds to the void room only.
            await loader.ReloadAsync();

            // World content was destroyed (the previously-spawned room/area are gone).
            Assert.Null(FindEntityByBlueprint(ecs, roomBpId));
            Assert.Null(FindEntityByBlueprint(ecs, areaBpId));

            // The persistent entity survived the teardown.
            Assert.True(ecs.HasComponent<PersistentEntity>(player.Id),
                "Persistent player entity must survive a reload rebuild.");
            Assert.True(ecs.HasComponent<BlueprintComponent>(player.Id));
        }
    }
}
