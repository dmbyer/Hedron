using System.Collections.Concurrent;
using System.Text.Json;
using Hedron.Core.ECS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Core system that persists <c>[Persistent]</c>-tagged components for every dirty entity.
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
    /// <b>Flush policy:</b> best-effort — a serialization failure for one entity is logged and
    /// the entity stays dirty for the next flush attempt.
    /// <b>Atomic write:</b> component files are written to <c>{id}.tmp</c> then renamed, so
    /// a crash mid-write never leaves a half-written file.
    /// <b>No event publishing.</b> This is a pure Core System. All event publishing (EntityHydratedEvent,
    /// WorldLoadedEvent, EntityPersistedEvent) is the responsibility of the calling orchestrator
    /// (<c>PersistenceBootstrap</c>). <see cref="LoadAllAsync"/> returns the IDs of restored entities
    /// so the orchestrator can fire per-entity events.
    /// </remarks>
    public sealed class PersistenceSystem : IPersistenceSystem
    {
        private readonly EntityService _entityService;
        private readonly IComponentTypeRegistry _typeRegistry;
        private readonly IComponentSerializer _serializer;
        private readonly ILogger<PersistenceSystem> _logger;
        private readonly string _dataDirectory;

        // Per-entity dirty set.  byte value is unused — ConcurrentDictionary<uint, byte>
        // gives us O(1) add/remove/check with no lock on the hot path.
        private readonly ConcurrentDictionary<uint, byte> _dirtySet = new();

        // Serializer options for the outer entity-snapshot envelope only.
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

        // ── Dirty-tracking ────────────────────────────────────────────────────

        /// <inheritdoc/>
        public void MarkDirty(uint entityId)
            => _dirtySet.TryAdd(entityId, 0);

        /// <inheritdoc/>
        public bool IsDirty(uint entityId)
            => _dirtySet.ContainsKey(entityId);

        // ── Flush ─────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task FlushAsync(CancellationToken ct = default)
        {
            // Snapshot the dirty set so we don't hold a lock across I/O.
            var snapshot = _dirtySet.Keys.ToArray();
            if (snapshot.Length == 0)
                return;

            EnsureDataDirectory();

            foreach (var entityId in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await WriteEntityAsync(entityId, ct);
                    _dirtySet.TryRemove(entityId, out _);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "PersistenceSystem: failed to flush entity {EntityId}; will retry on next flush.",
                        entityId);
                    // Entity stays dirty — best-effort policy.
                }
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Does not publish <c>EntityPersistedEvent</c> — that is the caller's responsibility.
        /// </remarks>
        public async Task SaveEntityAsync(uint entityId, CancellationToken ct = default)
        {
            EnsureDataDirectory();
            await WriteEntityAsync(entityId, ct);
            _dirtySet.TryRemove(entityId, out _);
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

        private async Task WriteEntityAsync(uint entityId, CancellationToken ct)
        {
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
            {
                // Entity has no persistent components — nothing to write.
                return;
            }

            var snapshot = new EntitySnapshot(entityId, components);
            var json = JsonSerializer.Serialize(snapshot, EnvelopeOptions);

            var finalPath = EntityFilePath(entityId);
            var tmpPath = finalPath + ".tmp";

            await File.WriteAllTextAsync(tmpPath, json, ct);
            File.Move(tmpPath, finalPath, overwrite: true);
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
                    // entry.Data is a JsonElement wrapping the serialized component JSON string.
                    // Unwrap the string first, then deserialize into the component type.
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

                    // Silent attachment — no event published during hydration.
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
