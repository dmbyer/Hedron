using System;
using System.IO;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items;
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
    /// Integration tests for the <c>reload</c> rebuild path (<see cref="WorldContentLoader.ReloadAsync"/>)
    /// using real YAML files on disk + the real serializer/deserializers. Proves that reload re-derives
    /// the world instance from YAML — edits take effect and picked-up world items respawn — while
    /// persistent (player-owned) entities survive the teardown.
    /// </summary>
    public sealed class WorldContentReloadTests : IDisposable
    {
        private readonly string _tempDir;

        public WorldContentReloadTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-reload-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(_tempDir, "rooms"));
            Directory.CreateDirectory(Path.Combine(_tempDir, "items"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }

        // ── Harness ──────────────────────────────────────────────────────────────

        private (WorldContentLoader loader, EntityService ecs, TemplateRegistry registry) Build()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var options = Options.Create(new WorldOptions
            {
                ContentDirectory = _tempDir,
                StartingRoomBlueprintId = "room.start",
            });
            var serializer = new YamlContentSerializer(new ITemplateDeserializer[]
            {
                new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance),
                new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance),
                new ItemTemplateDeserializer(NullLogger<ItemTemplateDeserializer>.Instance),
            });
            var loader = new WorldContentLoader(
                ecs,
                registry,
                serializer,
                new RoomContentWriter(options),
                new WorldConfiguration(),
                options,
                NullLogger<WorldContentLoader>.Instance);
            return (loader, ecs, registry);
        }

        private Task WriteRoom(string id, string name) =>
            File.WriteAllTextAsync(
                Path.Combine(_tempDir, "rooms", $"{id}.yaml"),
                $"id: {id}\nname: {name}\ndescription: A test room.\n");

        private Task WriteItem(string id, string name, string spawnRoomId) =>
            File.WriteAllTextAsync(
                Path.Combine(_tempDir, "items", $"{id}.yaml"),
                $"blueprintId: {id}\nname: {name}\ndescription: A test item.\nspawnRoomId: {spawnRoomId}\n");

        private static uint? FindByBlueprint(EntityService ecs, string blueprintId, uint exclude = 0u)
        {
            foreach (var (entityId, bp) in ecs.GetAllComponents<BlueprintComponent>())
            {
                if (entityId == exclude) continue;
                if (string.Equals(bp.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase))
                    return entityId;
            }
            return null;
        }

        // ── Reload picks up YAML edits to existing rooms ─────────────────────────

        [Fact]
        public async Task Reload_PicksUpEditsToExistingRooms()
        {
            await WriteRoom("room.start", "Old Name");

            var (loader, ecs, _) = Build();
            await loader.LoadAndSpawnAsync();

            var roomBefore = FindByBlueprint(ecs, "room.start")!.Value;
            Assert.Equal("Old Name", ecs.Get<RoomComponent>(roomBefore).Name);

            // Author an edit, then reload.
            await WriteRoom("room.start", "New Name");
            await loader.ReloadAsync();

            var roomAfter = FindByBlueprint(ecs, "room.start")!.Value;
            Assert.Equal("New Name", ecs.Get<RoomComponent>(roomAfter).Name);
        }

        // ── Reload respawns a picked-up world item; player copy preserved ────────

        [Fact]
        public async Task Reload_RespawnsPickedUpWorldItem_AndPreservesPlayerCopy()
        {
            await WriteRoom("room.start", "Starting Room");
            await WriteItem("item.sword", "sword", "room.start");

            var (loader, ecs, _) = Build();
            await loader.LoadAndSpawnAsync();

            // The authored world item is placed in the room on first load.
            var worldItem = FindByBlueprint(ecs, "item.sword")!.Value;
            Assert.True(ecs.HasComponent<LocationComponent>(worldItem));

            // Simulate a player picking it up: the same entity becomes player-owned (persistent),
            // leaves the room (no LocationComponent), and lands in the player's inventory.
            var player = ecs.CreateEntity();
            ecs.AddComponent(player.Id, new PersistentEntity());
            ecs.AddComponent(player.Id, new InventoryComponent());
            ecs.RemoveComponent<LocationComponent>(worldItem);
            ecs.AddComponent(worldItem, new PersistentEntity());
            ecs.Get<InventoryComponent>(player.Id).ItemEntityIds.Add(worldItem);

            // Reload rebuilds the world from YAML.
            await loader.ReloadAsync();

            // The picked-up copy is untouched: still persistent, still no room location.
            Assert.True(ecs.HasComponent<PersistentEntity>(worldItem));
            Assert.False(ecs.HasComponent<LocationComponent>(worldItem));

            // A FRESH world copy respawned into the (re-spawned) room — non-persistent, placed.
            var respawned = FindByBlueprint(ecs, "item.sword", exclude: worldItem);
            Assert.True(respawned.HasValue,
                "The picked-up world item should respawn in its room on reload.");
            Assert.False(ecs.HasComponent<PersistentEntity>(respawned!.Value));
            Assert.True(ecs.HasComponent<LocationComponent>(respawned.Value));
            var roomAfter = FindByBlueprint(ecs, "room.start")!.Value;
            Assert.Equal(roomAfter, ecs.Get<LocationComponent>(respawned.Value).RoomEntityId);

            // The player survived the rebuild.
            Assert.True(ecs.HasComponent<PersistentEntity>(player.Id));
        }
    }
}
