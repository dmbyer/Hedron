using System;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Persistence;
using Hedron.Core.Systems;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Harness
{
    /// <summary>
    /// Wires a real <see cref="PersistenceSystem"/> against an in-memory SQLite database so
    /// persistence round-trips can be tested without touching the file system.
    /// <para>
    /// The `:memory:` database only lives as long as at least one open connection references it.
    /// This harness opens a "keeper" connection with a unique shared-cache name and configures
    /// <see cref="PersistenceSystem"/> with the same URI so both connections share the same db.
    /// </para>
    /// Implements <see cref="IDisposable"/> — dispose after each test to close the keeper.
    /// </summary>
    public sealed class PersistenceTestHarness : IDisposable
    {
        private readonly SqliteConnection _keeper;
        private readonly string _sharedUri;
        private bool _disposed;

        public EntityService EntityService { get; }
        public PersistenceSystem PersistenceSystem { get; }

        public PersistenceTestHarness()
        {
            // Unique name per harness instance keeps parallel tests isolated.
            var dbName = $"hedron_test_{Guid.NewGuid():N}";
            _sharedUri = $"file:{dbName}?mode=memory&cache=shared";

            _keeper = new SqliteConnection($"Data Source={_sharedUri}");
            _keeper.Open();

            EntityService = new EntityService();

            var registry = new ComponentTypeRegistry();
            var serializer = new ComponentSerializer(registry);

            PersistenceSystem = new PersistenceSystem(
                EntityService,
                registry,
                serializer,
                Options.Create(new PersistenceOptions { DatabasePath = _sharedUri }),
                NullLogger<PersistenceSystem>.Instance);
        }

        /// <summary>
        /// Saves a single entity. Shortcut over <see cref="PersistenceSystem.SaveEntityAsync"/>.
        /// </summary>
        public Task SaveAsync(uint entityId)
            => PersistenceSystem.SaveEntityAsync(entityId);

        /// <summary>
        /// Creates a fresh <see cref="EntityService"/> and loads all persisted entities into it,
        /// reusing the same in-memory database via a new <see cref="PersistenceSystem"/> instance
        /// that shares the keeper's connection URI.
        /// </summary>
        public async Task<EntityService> ReloadIntoFreshWorld()
        {
            var freshEcs = new EntityService();
            var registry = new ComponentTypeRegistry();
            var serializer = new ComponentSerializer(registry);

            using var reloadSystem = new PersistenceSystem(
                freshEcs,
                registry,
                serializer,
                Options.Create(new PersistenceOptions { DatabasePath = _sharedUri }),
                NullLogger<PersistenceSystem>.Instance);

            await reloadSystem.LoadAllAsync();
            return freshEcs;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            PersistenceSystem.Dispose();
            _keeper.Dispose();
        }
    }

    // ── Self-test ────────────────────────────────────────────────────────────────

    public sealed class PersistenceTestHarnessTests
    {
        [Fact]
        public async Task Save_then_reload_preserves_Persistent_component()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            // Build a player entity with PersistentEntity opt-in and a [Persistent] component.
            var id = new EntityBuilder(ecs)
                .WithPools(hp: 75, mana: 30, stamina: 25, astra: 5)
                .Build();
            ecs.AddComponent(id, new PersistentEntity());

            await harness.SaveAsync(id);

            var fresh = await harness.ReloadIntoFreshWorld();

            Assert.True(fresh.HasComponent<PoolsComponent>(id));
            var pools = fresh.Get<PoolsComponent>(id);
            Assert.Equal(75, pools.CurrentHp);
            Assert.Equal(30, pools.CurrentMana);
        }

        [Fact]
        public async Task World_content_without_PersistentEntity_is_not_saved()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            // No PersistentEntity — SaveEntityAsync should no-op.
            var id = new EntityBuilder(ecs).WithPools(hp: 50).Build();
            await harness.SaveAsync(id);

            var fresh = await harness.ReloadIntoFreshWorld();
            Assert.False(fresh.HasComponent<PoolsComponent>(id));
        }
    }
}
