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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        private const string VoidRoomBlueprintId = StartingRoomResolver.VoidRoomBlueprintId;

        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IContentSerializer _serializer;
        private readonly IRoomContentWriter _roomContentWriter;
        private readonly IWorldManifestStore _manifestStore;
        private readonly Hedron.Core.WorldConfiguration _worldConfig;
        private readonly ILogger<WorldContentLoader> _logger;
        private readonly string _contentDirectory;
        private readonly string _startingRoomBlueprintId;

        public WorldContentLoader(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            IContentSerializer serializer,
            IRoomContentWriter roomContentWriter,
            IWorldManifestStore manifestStore,
            Hedron.Core.WorldConfiguration worldConfig,
            IOptions<WorldOptions> options,
            ILogger<WorldContentLoader> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _serializer = serializer;
            _roomContentWriter = roomContentWriter;
            _manifestStore = manifestStore;
            _worldConfig = worldConfig;
            _logger = logger;
            _contentDirectory = options.Value.ContentDirectory;
            _startingRoomBlueprintId = options.Value.StartingRoomBlueprintId;
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
                SpawnAndPlaceWorld();
            }

            ResolveStartingRoom();
        }

        /// <summary>
        /// Full rebuild of the live world instance — <b>not</b> additive. Tears down every live
        /// world-content entity, re-reads the YAML templates, and re-spawns the world from scratch,
        /// the same path <see cref="LoadAndSpawnAsync"/> takes at startup. Persistent entities
        /// (players and player-owned items/containers) are preserved; the caller re-publishes
        /// <c>WorldContentReadyEvent</c> after this returns so the post-load fan-out re-runs —
        /// shops re-seed their till + base stock, spawn slots rebuild, and
        /// <c>CharacterHydrationHandler</c> re-resolves each player's <c>RoomBlueprintId</c> to the
        /// fresh <c>RoomEntityId</c> (resetting to the starting room if their room was removed from
        /// YAML). This makes <c>reload</c> reset runtime instance state — picked-up world items
        /// respawn, depleted shop stock refills — exactly like a restart, without dropping players.
        /// </summary>
        public async Task<ContentReloadResult> ReloadAsync(CancellationToken ct = default)
        {
            var previousIds = new HashSet<string>(_templateRegistry.AllBlueprintIds(), StringComparer.OrdinalIgnoreCase);

            // 1. Tear down the current world instance (persistent entities survive).
            DestroyWorldContent();

            // 2. Re-read templates from YAML.
            _templateRegistry.Clear();
            await LoadTemplatesAsync(ct).ConfigureAwait(false);

            var currentIds = new HashSet<string>(_templateRegistry.AllBlueprintIds(), StringComparer.OrdinalIgnoreCase);
            var loaded = currentIds.Except(previousIds, StringComparer.OrdinalIgnoreCase).Count();
            var unchanged = currentIds.Intersect(previousIds, StringComparer.OrdinalIgnoreCase).Count();
            var removed = previousIds.Except(currentIds, StringComparer.OrdinalIgnoreCase).Count();

            // 3. Re-spawn the world fresh from templates.
            if (_templateRegistry.AllBlueprintIds().Count == 0)
            {
                SeedVoidRoom();
                if (_templateRegistry.TryGet(VoidRoomBlueprintId, out var voidTpl) &&
                    voidTpl is RoomTemplate voidRoomTpl)
                    await _roomContentWriter.WriteAsync(voidRoomTpl, ct).ConfigureAwait(false);
            }
            else
            {
                SpawnAndPlaceWorld();
            }

            ResolveStartingRoom();

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

        public ResolvedStartingRoom ResolveStartingRoom()
        {
            var read = _manifestStore.Read();
            if (read.Error is not null)
                _logger.LogError(
                    "WorldContentLoader: world manifest '{File}' is malformed and is treated as empty: {Error}",
                    _manifestStore.FilePath, read.Error);
            else if (read.Manifest.SchemaVersion is { } version && version != WorldManifest.CurrentSchemaVersion)
                _logger.LogWarning(
                    "WorldContentLoader: world manifest '{File}' declares schemaVersion {Version}; this engine reads {Expected}.",
                    _manifestStore.FilePath, version, WorldManifest.CurrentSchemaVersion);

            var rooms = BuildLiveRoomMap();
            var decision = StartingRoomResolver.Resolve(
                read.Manifest.StartingRoomId, _startingRoomBlueprintId, rooms.Keys);

            foreach (var warning in decision.Warnings)
                _logger.LogWarning("WorldContentLoader: {Warning}", warning);
            if (decision.Source == StartingRoomSource.Unresolved)
                _logger.LogError("WorldContentLoader: no rooms available to resolve a starting room.");

            var entityId = decision.BlueprintId is not null && rooms.TryGetValue(decision.BlueprintId, out var id) ? id : 0u;
            var resolved = new ResolvedStartingRoom(entityId, decision.BlueprintId, decision.Source, read.Manifest.StartingRoomId);
            _worldConfig.StartingRoom = resolved;
            return resolved;
        }

        private void LinkRoomAreas(Dictionary<string, uint> liveBlueprints)
        {
            foreach (var (entityId, room) in _entityService.GetAllComponents<RoomComponent>())
            {
                if (!_entityService.TryGet<BlueprintComponent>(entityId, out var bp))
                    continue;
                if (!_templateRegistry.TryGet(bp.BlueprintId, out var template) ||
                    template is not RoomTemplate roomTemplate)
                    continue;
                if (string.IsNullOrEmpty(roomTemplate.AreaId))
                    continue;
                if (liveBlueprints.TryGetValue(roomTemplate.AreaId, out var areaEntityId))
                    room.AreaEntityId = areaEntityId;
                else
                    _logger.LogWarning(
                        "WorldContentLoader: room '{RoomBp}' references unknown area blueprint '{AreaBp}' — AreaEntityId left 0.",
                        bp.BlueprintId, roomTemplate.AreaId);
            }
        }

        /// <summary>
        /// Spawns a live entity for every registered template that has no live world counterpart,
        /// then links exits, places items/mobs in their rooms, and links rooms to areas. Shared by
        /// the startup load (<see cref="LoadAndSpawnAsync"/>) and the <see cref="ReloadAsync"/>
        /// rebuild so both follow an identical spawn/place/link path.
        /// </summary>
        private void SpawnAndPlaceWorld()
        {
            var liveBlueprints = BuildLiveBlueprintMap();
            var newlySpawned = SpawnMissingEntities(liveBlueprints);
            LinkRoomExits(liveBlueprints);
            PlaceItemsInRooms(liveBlueprints, newlySpawned);
            PlaceMobsInRooms(liveBlueprints, newlySpawned);
            LinkRoomAreas(liveBlueprints);
        }

        /// <summary>
        /// Tears down the live world-content instance ahead of a <see cref="ReloadAsync"/> rebuild:
        /// destroys every entity carrying a <see cref="BlueprintComponent"/> but not
        /// <see cref="PersistentEntity"/> — rooms, areas, mobs, world/dropped items, and shop base
        /// stock / buy-back items. Persistent entities (players and player-owned items/containers)
        /// are preserved; their now-stale <c>RoomEntityId</c> is re-resolved from the durable
        /// <c>RoomBlueprintId</c> by <c>CharacterHydrationHandler</c> after the world re-spawns.
        /// </summary>
        private void DestroyWorldContent()
        {
            // Snapshot first — DestroyEntity mutates the component store we are iterating.
            var worldEntities = new List<uint>();
            foreach (var (entityId, _) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (_entityService.HasComponent<PersistentEntity>(entityId)) continue;
                worldEntities.Add(entityId);
            }

            foreach (var entityId in worldEntities)
                _entityService.DestroyEntity(entityId);
        }

        /// <summary>
        /// Maps blueprint id → live world <b>room</b> entity (<see cref="RoomComponent"/> +
        /// <see cref="BlueprintComponent"/>, excluding <see cref="PersistentEntity"/>) — the candidate
        /// set for starting-room resolution, so a non-room id (a mob, an item) never "resolves".
        /// </summary>
        private Dictionary<string, uint> BuildLiveRoomMap()
        {
            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var (entityId, _) in _entityService.GetAllComponents<RoomComponent>())
            {
                if (_entityService.HasComponent<PersistentEntity>(entityId)) continue;
                if (!_entityService.TryGet<BlueprintComponent>(entityId, out var bp) ||
                    string.IsNullOrEmpty(bp.BlueprintId)) continue;
                map[bp.BlueprintId] = entityId;
            }
            return map;
        }

        /// <summary>
        /// Maps blueprint id → the live <b>world</b> entity for that blueprint. Entities carrying
        /// <see cref="PersistentEntity"/> are excluded: a persisted entity sharing a blueprint id is a
        /// player-owned <i>instance</i> (it kept its <see cref="BlueprintComponent"/> as an origin
        /// record on pickup, INV-21) — not the authored world entity. Excluding them keeps world-content
        /// spawning/placement (the only consumers of this map) independent of player state, so authored
        /// content always re-spawns from YAML and is placed even when a player owns a copy of the same
        /// blueprint (e.g. a picked-up authored item, or gear stored in a player container). World
        /// content never opts into persistence (INV-23), so a world entity is never excluded here.
        /// </summary>
        private Dictionary<string, uint> BuildLiveBlueprintMap()
        {
            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var (entityId, blueprint) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (string.IsNullOrEmpty(blueprint.BlueprintId)) continue;
                if (_entityService.HasComponent<PersistentEntity>(entityId)) continue;
                map[blueprint.BlueprintId] = entityId;
            }
            return map;
        }
    }
}
