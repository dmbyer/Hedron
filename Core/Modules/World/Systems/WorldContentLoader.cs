using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Default <see cref="IWorldContentLoader"/>. Reads area and room YAML files from the
    /// configured content directory, registers templates, and seeds entities into the world.
    /// All world content (rooms, areas, mobs, items) is always fresh-spawned from YAML on
    /// startup — no SQLite rows exist for world content.
    /// </summary>
    public sealed class WorldContentLoader : IWorldContentLoader
    {
        private const string AreasSubdirectory = "areas";
        private const string RoomsSubdirectory = "rooms";
        private const string ItemsSubdirectory = "items";
        private const string MobsSubdirectory = "mobs";
        private const string VoidRoomBlueprintId = "room.void";

        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IContentSerializer _serializer;
        private readonly IRoomContentWriter _roomContentWriter;
        private readonly Hedron.Core.WorldConfiguration _worldConfig;
        private readonly ILogger<WorldContentLoader> _logger;
        private readonly string _contentDirectory;
        private readonly string _startingRoomBlueprintId;

        public WorldContentLoader(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            IContentSerializer serializer,
            IRoomContentWriter roomContentWriter,
            Hedron.Core.WorldConfiguration worldConfig,
            IConfiguration configuration,
            ILogger<WorldContentLoader> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _serializer = serializer;
            _roomContentWriter = roomContentWriter;
            _worldConfig = worldConfig;
            _logger = logger;
            _contentDirectory = configuration["World:ContentDirectory"] ?? "data/content/";
            _startingRoomBlueprintId = configuration["World:StartingRoomBlueprintId"] ?? "room.crossroads";
        }

        public async Task LoadAndSpawnAsync(CancellationToken ct = default)
        {
            await LoadTemplatesAsync(ct).ConfigureAwait(false);

            if (_templateRegistry.AllBlueprintIds().Count == 0)
            {
                _logger.LogWarning(
                    "WorldContentLoader: content directory '{Dir}' is missing or empty — seeding void room.",
                    _contentDirectory);
                SeedVoidRoom();

                // Write YAML immediately so the void room has a content file on first startup.
                if (_templateRegistry.TryGet(VoidRoomBlueprintId, out var voidTpl) &&
                    voidTpl is RoomTemplate voidRoomTpl)
                    await _roomContentWriter.WriteAsync(voidRoomTpl, ct).ConfigureAwait(false);
            }
            else
            {
                var liveBlueprints = BuildLiveBlueprintMap();
                var newlySpawned = SpawnMissingEntities(liveBlueprints);
                LinkRoomExits(liveBlueprints);
                PlaceItemsInRooms(liveBlueprints, newlySpawned);
                PlaceMobsInRooms(liveBlueprints, newlySpawned);
            }

            ResolveStartingRoom();
        }

        public async Task<ContentReloadResult> ReloadAsync(CancellationToken ct = default)
        {
            var previousIds = new HashSet<string>(_templateRegistry.AllBlueprintIds(), StringComparer.OrdinalIgnoreCase);
            _templateRegistry.Clear();

            await LoadTemplatesAsync(ct).ConfigureAwait(false);

            var currentIds = new HashSet<string>(_templateRegistry.AllBlueprintIds(), StringComparer.OrdinalIgnoreCase);
            var loaded = currentIds.Except(previousIds, StringComparer.OrdinalIgnoreCase).Count();
            var unchanged = currentIds.Intersect(previousIds, StringComparer.OrdinalIgnoreCase).Count();
            var removed = previousIds.Except(currentIds, StringComparer.OrdinalIgnoreCase).Count();

            // Additive only: spawn any template that has no live counterpart.
            var liveBlueprints = BuildLiveBlueprintMap();
            var newlySpawned = SpawnMissingEntities(liveBlueprints);
            LinkRoomExits(liveBlueprints);
            PlaceItemsInRooms(liveBlueprints, newlySpawned);
            PlaceMobsInRooms(liveBlueprints, newlySpawned);

            return new ContentReloadResult(loaded, unchanged, removed);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private async Task LoadTemplatesAsync(CancellationToken ct)
        {
            if (!Directory.Exists(_contentDirectory))
            {
                _logger.LogWarning(
                    "WorldContentLoader: content directory '{Dir}' does not exist.", _contentDirectory);
                return;
            }

            await LoadKindAsync("area", AreasSubdirectory, ct).ConfigureAwait(false);
            await LoadKindAsync("room", RoomsSubdirectory, ct).ConfigureAwait(false);
            await LoadKindAsync("item", ItemsSubdirectory, ct).ConfigureAwait(false);
            await LoadKindAsync("mob", MobsSubdirectory, ct).ConfigureAwait(false);
        }

        private async Task LoadKindAsync(string kind, string subdirectory, CancellationToken ct)
        {
            var directory = Path.Combine(_contentDirectory, subdirectory);
            if (!Directory.Exists(directory))
                return;

            var files = Directory.GetFiles(directory, "*" + _serializer.FormatExtension);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var body = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    var template = _serializer.Deserialize(kind, body);
                    _templateRegistry.Register(template.BlueprintId, template);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "WorldContentLoader: failed to load {Kind} file '{File}'; skipping.",
                        kind, file);
                }
            }
        }

        private void SeedVoidRoom()
        {
            var voidTemplate = new RoomTemplate(VoidRoomBlueprintId)
            {
                Name = "The Void",
                Description = "A featureless grey expanse. No content has been authored yet — use dig to start building.",
            };
            _templateRegistry.Register(VoidRoomBlueprintId, voidTemplate);
            _templateRegistry.Spawn(VoidRoomBlueprintId);
        }

        /// <summary>
        /// Spawns a live entity for every registered blueprint that has no current live entity.
        /// Returns the set of newly-spawned entity IDs so placement helpers can avoid
        /// overwriting existing location data on <c>ReloadAsync</c>.
        /// </summary>
        private HashSet<uint> SpawnMissingEntities(Dictionary<string, uint> liveBlueprints)
        {
            var newlySpawned = new HashSet<uint>();
            foreach (var blueprintId in _templateRegistry.AllBlueprintIds())
            {
                if (liveBlueprints.ContainsKey(blueprintId))
                    continue;
                var spawned = _templateRegistry.Spawn(blueprintId);
                liveBlueprints[blueprintId] = spawned.Id;
                newlySpawned.Add(spawned.Id);
            }
            return newlySpawned;
        }

        private void LinkRoomExits(Dictionary<string, uint> liveBlueprints)
        {
            foreach (var blueprintId in _templateRegistry.AllBlueprintIds())
            {
                if (!_templateRegistry.TryGet(blueprintId, out var template))
                    continue;
                if (template is not RoomTemplate roomTemplate)
                    continue;
                if (!liveBlueprints.TryGetValue(blueprintId, out var roomEntityId))
                    continue;
                if (!_entityService.TryGet<RoomComponent>(roomEntityId, out var room))
                    continue;

                room.Exits.Clear();
                foreach (var (direction, targetBlueprintId) in roomTemplate.Exits)
                {
                    if (!liveBlueprints.TryGetValue(targetBlueprintId, out var targetEntityId))
                    {
                        _logger.LogWarning(
                            "WorldContentLoader: room '{Source}' references unknown exit target '{Target}' — exit unlinked.",
                            blueprintId, targetBlueprintId);
                        continue;
                    }
                    room.Exits[direction] = targetEntityId;
                }
            }
        }

        /// <summary>
        /// Attaches initial <see cref="LocationComponent"/> (both entity ID and blueprint ID) to
        /// item entities that were just spawned in this pass. Restored-from-persistence entities
        /// and entities already carrying a <c>LocationComponent</c> are skipped.
        /// </summary>
        private void PlaceItemsInRooms(Dictionary<string, uint> liveBlueprints, HashSet<uint> newlySpawned)
        {
            foreach (var blueprintId in _templateRegistry.AllBlueprintIds())
            {
                if (!_templateRegistry.TryGet(blueprintId, out var template) ||
                    template is not ItemTemplate itemTemplate)
                    continue;

                if (!liveBlueprints.TryGetValue(blueprintId, out var entityId))
                    continue;

                if (!newlySpawned.Contains(entityId))
                    continue;

                if (string.IsNullOrEmpty(itemTemplate.SpawnRoomBlueprintId))
                    continue;

                if (!liveBlueprints.TryGetValue(itemTemplate.SpawnRoomBlueprintId, out var roomEntityId))
                {
                    _logger.LogWarning(
                        "WorldContentLoader: item '{Blueprint}' references unknown spawnRoomId '{RoomBlueprint}' — item created without location.",
                        blueprintId, itemTemplate.SpawnRoomBlueprintId);
                    continue;
                }

                _entityService.AddComponent(entityId, new LocationComponent
                {
                    RoomEntityId = roomEntityId,
                    RoomBlueprintId = itemTemplate.SpawnRoomBlueprintId,
                });
            }
        }

        private void PlaceMobsInRooms(Dictionary<string, uint> liveBlueprints, HashSet<uint> newlySpawned)
        {
            foreach (var blueprintId in _templateRegistry.AllBlueprintIds())
            {
                if (!_templateRegistry.TryGet(blueprintId, out var template) ||
                    template is not MobTemplate mobTemplate)
                    continue;

                if (!liveBlueprints.TryGetValue(blueprintId, out var entityId))
                    continue;

                if (!newlySpawned.Contains(entityId))
                {
                    if (!string.IsNullOrEmpty(mobTemplate.SpawnRoomBlueprintId) &&
                        liveBlueprints.TryGetValue(mobTemplate.SpawnRoomBlueprintId, out var expectedRoom) &&
                        _entityService.TryGet<LocationComponent>(entityId, out var existingLoc) &&
                        existingLoc.RoomEntityId != expectedRoom)
                    {
                        _logger.LogWarning(
                            "WorldContentLoader: mob '{Blueprint}' spawnRoomBlueprintId changed in YAML " +
                            "but the live entity is already placed — restart required to apply the new spawn room.",
                            blueprintId);
                    }
                    continue;
                }

                if (string.IsNullOrEmpty(mobTemplate.SpawnRoomBlueprintId))
                    continue;

                if (!liveBlueprints.TryGetValue(mobTemplate.SpawnRoomBlueprintId, out var roomEntityId))
                {
                    _logger.LogWarning(
                        "WorldContentLoader: mob '{Blueprint}' references unknown spawnRoomBlueprintId '{RoomBlueprint}' — mob created without location.",
                        blueprintId, mobTemplate.SpawnRoomBlueprintId);
                    continue;
                }

                _entityService.AddComponent(entityId, new LocationComponent
                {
                    RoomEntityId = roomEntityId,
                    RoomBlueprintId = mobTemplate.SpawnRoomBlueprintId,
                });
            }
        }

        private void ResolveStartingRoom()
        {
            var liveBlueprints = BuildLiveBlueprintMap();
            if (liveBlueprints.TryGetValue(_startingRoomBlueprintId, out var entityId))
            {
                _worldConfig.StartingRoomEntityId = entityId;
                _worldConfig.StartingRoomBlueprintId = _startingRoomBlueprintId;
                return;
            }

            if (liveBlueprints.TryGetValue(VoidRoomBlueprintId, out var voidEntityId))
            {
                _logger.LogWarning(
                    "WorldContentLoader: configured starting room '{Id}' not found; falling back to void room.",
                    _startingRoomBlueprintId);
                _worldConfig.StartingRoomEntityId = voidEntityId;
                _worldConfig.StartingRoomBlueprintId = VoidRoomBlueprintId;
                return;
            }

            var anyRoom = liveBlueprints.FirstOrDefault(kvp => kvp.Key.StartsWith("room.", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(anyRoom.Key))
            {
                _logger.LogWarning(
                    "WorldContentLoader: configured starting room '{Id}' not found; falling back to '{Fallback}'.",
                    _startingRoomBlueprintId, anyRoom.Key);
                _worldConfig.StartingRoomEntityId = anyRoom.Value;
                _worldConfig.StartingRoomBlueprintId = anyRoom.Key;
            }
            else
            {
                _logger.LogError(
                    "WorldContentLoader: no rooms available to resolve starting room '{Id}'.",
                    _startingRoomBlueprintId);
            }
        }

        private Dictionary<string, uint> BuildLiveBlueprintMap()
        {
            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var (entityId, blueprint) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (string.IsNullOrEmpty(blueprint.BlueprintId)) continue;
                map[blueprint.BlueprintId] = entityId;
            }
            return map;
        }
    }
}
