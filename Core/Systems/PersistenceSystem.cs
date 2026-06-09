using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Core system that persists <c>[Persistent]</c>-tagged components for every entity
    /// that carries the <c>PersistentEntity</c> opt-in marker, backed by SQLite.
    /// </summary>
    /// <remarks>
    /// <b>SQLite schema:</b>
    /// <code>
    /// CREATE TABLE entity_components (
    ///   entity_id  INTEGER NOT NULL,
    ///   type_name  TEXT    NOT NULL,
    ///   data       TEXT    NOT NULL,
    ///   PRIMARY KEY (entity_id, type_name)
    /// );
    /// </code>
    /// <b>Two-level model.</b> An entity is written only if it carries <c>PersistentEntity</c>.
    /// Among its components, only those tagged <c>[Persistent]</c> are included in the snapshot.
    /// <b>Auto-delete.</b> Registers <c>EntityService.OnPersistentEntityDestroying</c> so that
    /// every <c>DestroyEntity</c> call for a persistent entity automatically issues a DELETE —
    /// no caller ever needs to clean up SQLite rows manually.
    /// <b>No event publishing.</b> This is a pure Core System. All event publishing is the
    /// responsibility of the calling orchestrator (<c>PersistenceBootstrap</c>).
    /// </remarks>
    public sealed class PersistenceSystem : IPersistenceSystem, IDisposable
    {
        private readonly EntityService _entityService;
        private readonly IComponentTypeRegistry _typeRegistry;
        private readonly IComponentSerializer _serializer;
        private readonly ILogger<PersistenceSystem> _logger;
        private readonly string _databasePath;

        private SqliteConnection? _connection;

        public PersistenceSystem(
            EntityService entityService,
            IComponentTypeRegistry typeRegistry,
            IComponentSerializer serializer,
            IOptions<PersistenceOptions> options,
            ILogger<PersistenceSystem> logger)
        {
            _entityService = entityService;
            _typeRegistry = typeRegistry;
            _serializer = serializer;
            _logger = logger;
            _databasePath = options.Value.DatabasePath;

            entityService.OnPersistentEntityDestroying = DeleteEntitySync;
        }

        // ── Save ──────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public Task SaveEntityAsync(uint entityId, CancellationToken ct = default)
        {
            EnsureConnection();
            using var tx = _connection!.BeginTransaction();
            WriteEntityToDb(entityId, tx);
            tx.Commit();
            return Task.CompletedTask;
        }

        // ── Flush ─────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public Task FlushDirtyAsync(CancellationToken ct = default)
            => FlushPersistentEntities(ct, "periodic flush");

        /// <inheritdoc/>
        public Task FlushAllAsync(CancellationToken ct = default)
            => FlushPersistentEntities(ct, "shutdown flush");

        // ── Load ──────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default)
        {
            EnsureConnection();

            var entityComponents = new Dictionary<uint, List<(string TypeName, string Data)>>();

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT entity_id, type_name, data FROM entity_components ORDER BY entity_id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                var entityId = (uint)(long)reader["entity_id"];
                var typeName = (string)reader["type_name"];
                var data = (string)reader["data"];

                if (!entityComponents.TryGetValue(entityId, out var list))
                    entityComponents[entityId] = list = [];
                list.Add((typeName, data));
            }

            _logger.LogInformation(
                "PersistenceSystem: loading {Count} entity/entities from SQLite.",
                entityComponents.Count);

            var loaded = new List<uint>(entityComponents.Count);
            foreach (var (entityId, components) in entityComponents)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var entity = _entityService.RestoreEntity(entityId);
                    foreach (var (typeName, data) in components)
                        RestoreComponent(entity.Id, typeName, data);
                    loaded.Add(entity.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "PersistenceSystem: failed to restore entity {EntityId}.", entityId);
                }
            }

            return Task.FromResult<IReadOnlyList<uint>>(loaded);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private Task FlushPersistentEntities(CancellationToken ct, string context)
        {
            EnsureConnection();

            var entityIds = _entityService.GetEntitiesWith<PersistentEntity>().ToList();
            var saved = 0;

            using var tx = _connection!.BeginTransaction();
            foreach (var entityId in entityIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    WriteEntityToDb(entityId, tx);
                    saved++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "PersistenceSystem: failed to flush entity {EntityId}.", entityId);
                }
            }
            tx.Commit();

            _logger.LogInformation(
                "PersistenceSystem: {Context} wrote {Saved}/{Total} entity/entities.",
                context, saved, entityIds.Count);

            return Task.CompletedTask;
        }

        private void WriteEntityToDb(uint entityId, SqliteTransaction tx)
        {
            if (!_entityService.HasComponent<PersistentEntity>(entityId))
                return;

            var components = _entityService
                .GetAllComponentsForEntity(entityId)
                .Where(pair => _typeRegistry.IsPersistent(pair.ComponentType))
                .Select(pair => (
                    TypeName: pair.ComponentType.FullName ?? pair.ComponentType.Name,
                    Data: _serializer.Serialize(pair.Component)))
                .ToList();

            if (components.Count == 0)
                return;

            using var deleteCmd = _connection!.CreateCommand();
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM entity_components WHERE entity_id = @id";
            deleteCmd.Parameters.AddWithValue("@id", (long)entityId);
            deleteCmd.ExecuteNonQuery();

            foreach (var (typeName, data) in components)
            {
                using var insertCmd = _connection.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText =
                    "INSERT INTO entity_components (entity_id, type_name, data) VALUES (@id, @type, @data)";
                insertCmd.Parameters.AddWithValue("@id", (long)entityId);
                insertCmd.Parameters.AddWithValue("@type", typeName);
                insertCmd.Parameters.AddWithValue("@data", data);
                insertCmd.ExecuteNonQuery();
            }
        }

        private void RestoreComponent(uint entityId, string typeName, string data)
        {
            try
            {
                var resolvedType = _typeRegistry.Resolve(typeName);
                if (resolvedType is null)
                {
                    _logger.LogWarning(
                        "PersistenceSystem: could not resolve component type '{TypeName}' — skipping.",
                        typeName);
                    return;
                }

                var component = _serializer.Deserialize(typeName, data);
                if (component is null) return;

                _entityService.AddComponent(entityId, resolvedType, component);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "PersistenceSystem: error restoring component '{TypeName}' for entity {EntityId}.",
                    typeName, entityId);
            }
        }

        private void DeleteEntitySync(uint entityId)
        {
            if (_connection is null) return;
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM entity_components WHERE entity_id = @id";
                cmd.Parameters.AddWithValue("@id", (long)entityId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "PersistenceSystem: failed to delete entity {EntityId} from SQLite.", entityId);
            }
        }

        private void EnsureConnection()
        {
            if (_connection != null) return;

            // Skip directory scaffolding for in-memory / URI paths (used in tests).
            if (!_databasePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                _databasePath != ":memory:")
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(_databasePath));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }

            _connection = new SqliteConnection($"Data Source={_databasePath}");
            _connection.Open();
            BootstrapSchema();

            _logger.LogInformation(
                "PersistenceSystem: opened SQLite database at '{Path}'.", _databasePath);
        }

        private void BootstrapSchema()
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS entity_components (
                    entity_id  INTEGER NOT NULL,
                    type_name  TEXT    NOT NULL,
                    data       TEXT    NOT NULL,
                    PRIMARY KEY (entity_id, type_name)
                );
                """;
            cmd.ExecuteNonQuery();
        }

        public void Dispose() => _connection?.Dispose();
    }
}
