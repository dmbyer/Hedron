using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
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

        // ── ReloadAsync: LinkRoomAreas is called; existing AreaEntityId preserved ─

        [Fact]
        public async Task WorldContentLoader_ReloadAsync_RelinkRoomAreas()
        {
            var (loader, ecs, registry) = Build();

            const string areaBpId = "core.area.midlands";
            const string roomBpId = "core.room.market";

            registry.Register(areaBpId, new AreaTemplate(areaBpId) { Name = "The Midlands" });
            registry.Register(roomBpId, new RoomTemplate(roomBpId) { Name = "Market", AreaId = areaBpId });

            // Initial load — area link established.
            await loader.LoadAndSpawnAsync();

            var areaEntityId = FindEntityByBlueprint(ecs, areaBpId);
            var roomEntityId = FindEntityByBlueprint(ecs, roomBpId);
            Assert.True(areaEntityId.HasValue);
            Assert.True(roomEntityId.HasValue);
            Assert.Equal(areaEntityId!.Value, ecs.Get<RoomComponent>(roomEntityId!.Value).AreaEntityId);

            // ReloadAsync: clears templates, content dir is nonexistent so nothing re-loads.
            // LinkRoomAreas is still called on the live entities.
            // Because the template was cleared, TryGet returns false and AreaEntityId is NOT cleared —
            // the prior assignment is preserved (only an explicit re-assignment would change it).
            var reloadResult = await loader.ReloadAsync();

            // After reload with empty template registry, the live room entity retains its area assignment.
            var roomAfterReload = ecs.Get<RoomComponent>(roomEntityId!.Value);
            Assert.Equal(areaEntityId!.Value, roomAfterReload.AreaEntityId);
        }
    }
}
