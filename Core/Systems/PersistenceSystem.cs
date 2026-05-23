using System.Collections.Generic;
using System.Text.Json;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Core system that persists <c>[Persistent]</c>-tagged components for every entity
    /// that carries the <c>PersistentEntity</c> opt-in marker.
    /// </summary>
    /// <remarks>
    /// <b>Entity snapshot format</b> (<c>entity-{id}.json</c>):
    /// <code>
    /// {
    ///   "entityId": 42,
    ///   "components": [
    ///     { "typeName": "Hedron.Core.ECS.Components.SomeComponent", "data": "{ ...json... }" }
    ///   ]
    /// }
    /// </code>
    /// <b>Two-level model.</b> An entity is written only if it carries <c>PersistentEntity</c>.
    /// Among its components, only those tagged <c>[Persistent]</c> are included in the snapshot.
    /// <b>Atomic write:</b> files are written to <c>{id}.tmp</c> then renamed, so a crash
    /// mid-write never leaves a half-written file.
    /// <b>No event publishing.</b> This is a pure Core System. All event publishing is the
    /// responsibility of the calling orchestrator (<c>PersistenceBootstrap</c>).
    /// </remarks>
    public sealed class PersistenceSystem : IPersistenceSystem
    {
        private readonly EntityService _entityService;
        private readonly IComponentTypeRegistry _typeRegistry;
        private readonly IComponentSerializer _serializer;
        private readonly ILogger<PersistenceSystem> _logger;
        private readonly string _dataDirectory;

        private static readonly JsonSerializerOptions EnvelopeOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        public PersistenceSystem(
            EntityService entityService,
            IComponentTypeRegistry typeRegistry,
            IComponentSerializer serializer,
            IConfiguration configuration,
            ILogger<PersistenceSystem> logger)
        {
            _entityService = entityService;
            _typeRegistry = typeRegistry;
            _serializer = serializer;
            _logger = logger;

            _dataDirectory = configuration["Persistence:DataDirectory"] ?? "data/entities/";
        }

        // ── Save ──────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task SaveEntityAsync(uint entityId, CancellationToken ct = default)
        {
            EnsureDataDirectory();
            await WriteEntityAsync(entityId, ct);
        }

        // ── Flush ─────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task FlushActivePlayerFootprintAsync(
            IEnumerable<uint> occupiedRoomIds, CancellationToken ct = default)
        {
            var roomSet = new HashSet<uint>(occupiedRoomIds);
            if (roomSet.Count == 0) return;

            EnsureDataDirectory();

            var entityIds = _entityService
                .GetAllComponents<LocationComponent>()
                .Where(pair => roomSet.Contains(pair.Component.RoomEntityId))
                .Select(pair => pair.EntityId)
                .ToList();

            var total = _entityService.GetAllComponents<PersistentEntity>().Count();

            var saved = 0;
            foreach (var entityId in entityIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (await WriteEntityAsync(entityId, ct))
                        saved++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "PersistenceSystem: failed to flush entity {EntityId} in footprint flush.",
                        entityId);
                }
            }

            _logger.LogInformation(
                "PersistenceSystem: periodic flush wrote {Saved}/{Total} entity/entities ({Rooms} occupied room(s)).",
                saved, total, roomSet.Count);
        }

        /// <inheritdoc/>
        public async Task FlushAllPersistentAsync(CancellationToken ct = default)
        {
            EnsureDataDirectory();

            var entityIds = _entityService
                .GetAllComponents<PersistentEntity>()
                .Select(pair => pair.EntityId)
                .ToList();

            var saved = 0;
            foreach (var entityId in entityIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (await WriteEntityAsync(entityId, ct))
                        saved++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "PersistenceSystem: failed to flush entity {EntityId} in shutdown flush.",
                        entityId);
                }
            }

            _logger.LogInformation(
                "PersistenceSystem: shutdown flush wrote {Saved}/{Total} entity/entities.",
                saved, entityIds.Count);
        }

        // ── Load ──────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default)
        {
            if (!Directory.Exists(_dataDirectory))
            {
                _logger.LogInformation(
                    "PersistenceSystem: data directory '{Dir}' not found — starting with empty world.",
                    _dataDirectory);
                return Array.Empty<uint>();
            }

            var files = Directory.GetFiles(_dataDirectory, "entity-*.json");
            _logger.LogInformation(
                "PersistenceSystem: loading {Count} entity file(s) from '{Dir}'.",
                files.Length, _dataDirectory);

            var loaded = new List<uint>(files.Length);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var entityId = await LoadEntityFileAsync(file, ct);
                    if (entityId.HasValue)
                        loaded.Add(entityId.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "PersistenceSystem: failed to load entity file '{File}'; skipping.",
                        file);
                }
            }

            return loaded;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Writes entity to disk. Returns <c>true</c> if written, <c>false</c> if skipped
        /// (entity lacks <c>PersistentEntity</c> marker or has no persistent components).
        /// </summary>
        private async Task<bool> WriteEntityAsync(uint entityId, CancellationToken ct)
        {
            if (!_entityService.HasComponent<PersistentEntity>(entityId))
                return false;

            var components = _entityService
                .GetAllComponentsForEntity(entityId)
                .Where(pair => _typeRegistry.IsPersistent(pair.ComponentType))
                .Select(pair => new ComponentEntry(
                    pair.ComponentType.FullName ?? pair.ComponentType.Name,
                    JsonSerializer.SerializeToElement(
                        _serializer.Serialize(pair.Component),
                        typeof(string),
                        EnvelopeOptions)))
                .ToList();

            if (components.Count == 0)
                return false;

            var snapshot = new EntitySnapshot(entityId, components);
            var json = JsonSerializer.Serialize(snapshot, EnvelopeOptions);

            var finalPath = EntityFilePath(entityId);
            var tmpPath = finalPath + ".tmp";

            await File.WriteAllTextAsync(tmpPath, json, ct);
            File.Move(tmpPath, finalPath, overwrite: true);
            return true;
        }

        private async Task<uint?> LoadEntityFileAsync(string filePath, CancellationToken ct)
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            var snapshot = JsonSerializer.Deserialize<EntitySnapshot>(json, EnvelopeOptions);
            if (snapshot is null)
            {
                _logger.LogWarning(
                    "PersistenceSystem: could not deserialize entity snapshot from '{File}'.", filePath);
                return null;
            }

            var entity = _entityService.RestoreEntity(snapshot.EntityId);

            foreach (var entry in snapshot.Components)
            {
                try
                {
                    var componentJson = entry.Data.GetString()
                        ?? throw new InvalidOperationException(
                            $"Component data for '{entry.TypeName}' is not a JSON string.");

                    var resolvedType = _typeRegistry.Resolve(entry.TypeName);
                    if (resolvedType is null)
                    {
                        _logger.LogWarning(
                            "PersistenceSystem: could not resolve component type '{TypeName}' — skipping.",
                            entry.TypeName);
                        continue;
                    }

                    var component = _serializer.Deserialize(entry.TypeName, componentJson);
                    if (component is null)
                        continue;

                    _entityService.AddComponent(entity.Id, resolvedType, component);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "PersistenceSystem: error restoring component '{TypeName}' for entity {EntityId}.",
                        entry.TypeName, snapshot.EntityId);
                }
            }

            return entity.Id;
        }

        private void EnsureDataDirectory()
        {
            if (!Directory.Exists(_dataDirectory))
                Directory.CreateDirectory(_dataDirectory);
        }

        private string EntityFilePath(uint entityId)
            => Path.Combine(_dataDirectory, $"entity-{entityId}.json");

        // ── Snapshot DTOs ─────────────────────────────────────────────────────

        private sealed record EntitySnapshot(uint EntityId, List<ComponentEntry> Components);
        private sealed record ComponentEntry(string TypeName, JsonElement Data);
    }
}
