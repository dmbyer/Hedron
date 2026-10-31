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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Default <see cref="IWorldContentLoader"/>. Reads area, room, item and mob YAML files from
    /// the configured content directory, registers templates, and spawns the <b>areas and rooms</b>.
    /// Mobs and found items are never placed here: room spawn rules are the single spawn truth, and
    /// <c>SpawnSystem</c> fills every slot on <c>WorldContentReadyEvent</c>. No SQLite rows exist
    /// for world content.
    /// </summary>
    /// <remarks>
    /// <b>Staged, fail-fast read.</b> Every content file is deserialized into a staging list before
    /// anything is registered or torn down. A <see cref="ContentShapeException"/> from any file is
    /// collected; if any were collected, <see cref="ContentLoadException"/> is thrown listing every
    /// offending file, so boot aborts and <c>reload</c> refuses as a whole with the live world
    /// untouched. Other per-file failures (e.g. malformed YAML) keep the log-and-skip posture.
    /// </remarks>
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
            var staged = await ReadCorpusAsync(ct).ConfigureAwait(false);
            RegisterStaged(staged);

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
        /// <para>
        /// Whole-or-nothing: the corpus is read and deserialized into a staging set <b>before</b>
        /// teardown. On a shape error <see cref="ContentLoadException"/> is thrown and the live
        /// world, the registry and the spawn tracker are left unchanged.
        /// </para>
        /// </summary>
        public async Task<ContentReloadResult> ReloadAsync(CancellationToken ct = default)
        {
            // 1. Stage and validate the whole corpus; throws before anything is touched.
            var staged = await ReadCorpusAsync(ct).ConfigureAwait(false);

            var previousIds = new HashSet<string>(_templateRegistry.AllBlueprintIds(), StringComparer.OrdinalIgnoreCase);

            // 2. Tear down the current world instance (persistent entities survive).
            DestroyWorldContent();

            // 3. Swap in the staged templates.
            _templateRegistry.Clear();
            RegisterStaged(staged);

            var currentIds = new HashSet<string>(_templateRegistry.AllBlueprintIds(), StringComparer.OrdinalIgnoreCase);
            var loaded = currentIds.Except(previousIds, StringComparer.OrdinalIgnoreCase).Count();
            var unchanged = currentIds.Intersect(previousIds, StringComparer.OrdinalIgnoreCase).Count();
            var removed = previousIds.Except(currentIds, StringComparer.OrdinalIgnoreCase).Count();

            // 4. Re-spawn the world fresh from templates.
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

        /// <summary>
        /// Deserializes every content file into a staging list without registering anything.
        /// Shape errors are collected (each prefixed <c>&lt;kind&gt;/&lt;file&gt;: </c>) and thrown
        /// together as <see cref="ContentLoadException"/>; other failures are logged and skipped.
        /// </summary>
        private async Task<List<IEntityTemplate>> ReadCorpusAsync(CancellationToken ct)
        {
            var staged = new List<IEntityTemplate>();
            var shapeErrors = new List<string>();

            if (!Directory.Exists(_contentDirectory))
            {
                _logger.LogWarning(
                    "WorldContentLoader: content directory '{Dir}' does not exist.", _contentDirectory);
                return staged;
            }

            await ReadKindAsync("area", AreasSubdirectory, staged, shapeErrors, ct).ConfigureAwait(false);
            await ReadKindAsync("room", RoomsSubdirectory, staged, shapeErrors, ct).ConfigureAwait(false);
            await ReadKindAsync("item", ItemsSubdirectory, staged, shapeErrors, ct).ConfigureAwait(false);
            await ReadKindAsync("mob", MobsSubdirectory, staged, shapeErrors, ct).ConfigureAwait(false);

            if (shapeErrors.Count > 0)
                throw new ContentLoadException(shapeErrors);

            return staged;
        }

        private async Task ReadKindAsync(
            string kind, string subdirectory, List<IEntityTemplate> staged, List<string> shapeErrors, CancellationToken ct)
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
                    staged.Add(_serializer.Deserialize(kind, body));
                }
                catch (ContentShapeException ex)
                {
                    shapeErrors.Add($"{subdirectory}/{Path.GetFileName(file)}: {ex.Message}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "WorldContentLoader: failed to load {Kind} file '{File}'; skipping.",
                        kind, file);
                }
            }
        }

        private void RegisterStaged(List<IEntityTemplate> staged)
        {
            foreach (var template in staged)
                _templateRegistry.Register(template.BlueprintId, template);
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
        /// Spawns a live entity for every registered <b>room and area</b> blueprint that has no
        /// current live entity. Mob and item templates are never spawned here — room spawn rules
        /// place them via <c>SpawnSystem</c>.
        /// </summary>
        private void SpawnMissingEntities(Dictionary<string, uint> liveBlueprints)
        {
            foreach (var blueprintId in _templateRegistry.AllBlueprintIds())
            {
                if (liveBlueprints.ContainsKey(blueprintId))
                    continue;
                if (!_templateRegistry.TryGet(blueprintId, out var template) ||
                    template is not (RoomTemplate or AreaTemplate))
                    continue;
                var spawned = _templateRegistry.Spawn(blueprintId);
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
        /// Spawns every area and room template that has no live world counterpart, then links
        /// exits and links rooms to areas. Shared by the startup load (<see cref="LoadAndSpawnAsync"/>)
        /// and the <see cref="ReloadAsync"/> rebuild so both follow an identical spawn/link path.
        /// Mobs and found items are placed afterwards by <c>SpawnSystem</c>'s boot fill.
        /// </summary>
        private void SpawnAndPlaceWorld()
        {
            var liveBlueprints = BuildLiveBlueprintMap();
            SpawnMissingEntities(liveBlueprints);
            LinkRoomExits(liveBlueprints);
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
        /// spawning/linking (the only consumers of this map) independent of player state. World
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
