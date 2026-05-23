using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Default <see cref="IWorldContentLoader"/>. Reads area and room YAML files from the
    /// configured content directory, registers templates, and seeds entities into the
    /// world for blueprints that aren't already represented by a hydrated entity.
    /// </summary>
    /// <remarks>
    /// <b>Startup ordering.</b> <c>WorldContentBootstrap</c> ensures
    /// <see cref="LoadAndSpawnAsync"/> runs after <c>PersistenceBootstrap</c> has finished —
    /// every persisted entity is already in <see cref="EntityService"/> by the time we
    /// begin our spawn pass, so the conflict check (skip blueprint id that already has a
    /// live entity) sees the full hydrated world.
    /// </remarks>
    public sealed class WorldContentLoader : IWorldContentLoader
    {
        private const string AreasSubdirectory = "areas";
        private const string RoomsSubdirectory = "rooms";
        private const string VoidRoomBlueprintId = "room.void";

        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IContentSerializer _serializer;
        private readonly Hedron.Core.WorldConfiguration _worldConfig;
        private readonly ILogger<WorldContentLoader> _logger;
        private readonly string _contentDirectory;
        private readonly string _startingRoomBlueprintId;

        public WorldContentLoader(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            IContentSerializer serializer,
            Hedron.Core.WorldConfiguration worldConfig,
            IConfiguration configuration,
            ILogger<WorldContentLoader> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _serializer = serializer;
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
            }
            else
            {
                // Build the blueprint→entity map once and thread it through the spawn-then-link
                // sequence so the two steps see a consistent view (and we don't pay for three
                // GetAllComponents passes during startup).
                var liveBlueprints = BuildLiveBlueprintMap();
                SpawnMissingEntities(liveBlueprints);
                LinkRoomExits(liveBlueprints);
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

            // Additive only: spawn any template that has no live counterpart. Existing live
            // entities are not touched.
            var liveBlueprints = BuildLiveBlueprintMap();
            SpawnMissingEntities(liveBlueprints);
            LinkRoomExits(liveBlueprints);

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
                Description = "A featureless grey expanse. No content has been authored yet — use @dig to start building.",
            };
            _templateRegistry.Register(VoidRoomBlueprintId, voidTemplate);
            var spawned = _templateRegistry.Spawn(VoidRoomBlueprintId);
            _entityService.AddComponent(spawned.Id, new PersistentEntity());
        }

        private void SpawnMissingEntities(Dictionary<string, uint> liveBlueprints)
        {
            foreach (var blueprintId in _templateRegistry.AllBlueprintIds())
            {
                if (liveBlueprints.ContainsKey(blueprintId))
                    continue;
                var spawned = _templateRegistry.Spawn(blueprintId);
                _entityService.AddComponent(spawned.Id, new PersistentEntity());
                liveBlueprints[blueprintId] = spawned.Id;
            }
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

        private void ResolveStartingRoom()
        {
            var liveBlueprints = BuildLiveBlueprintMap();
            if (liveBlueprints.TryGetValue(_startingRoomBlueprintId, out var entityId))
            {
                _worldConfig.StartingRoomEntityId = entityId;
                return;
            }

            // Fall back to the void room (which always exists in the empty-content fallback path).
            if (liveBlueprints.TryGetValue(VoidRoomBlueprintId, out var voidEntityId))
            {
                _logger.LogWarning(
                    "WorldContentLoader: configured starting room '{Id}' not found; falling back to void room.",
                    _startingRoomBlueprintId);
                _worldConfig.StartingRoomEntityId = voidEntityId;
                return;
            }

            // Last-ditch: pick any room that exists, otherwise leave starting room unset.
            var anyRoom = liveBlueprints.FirstOrDefault(kvp => kvp.Key.StartsWith("room.", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(anyRoom.Key))
            {
                _logger.LogWarning(
                    "WorldContentLoader: configured starting room '{Id}' not found; falling back to '{Fallback}'.",
                    _startingRoomBlueprintId, anyRoom.Key);
                _worldConfig.StartingRoomEntityId = anyRoom.Value;
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
