using Hedron.Core.Events;
using Hedron.Core.Modules.Persistence.Events;
using Hedron.Core.Systems;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hedron.Server
{
    /// <summary>
    /// Hosted service that drives the full entity-load sequence at startup and a final flush
    /// at shutdown.
    /// </summary>
    /// <remarks>
    /// <b>Registration order matters.</b> <c>PersistenceBootstrap</c> must be registered before
    /// <c>TelnetServer</c> in <c>Program.cs</c> so that <see cref="StartAsync"/> (which loads
    /// all entities and publishes <see cref="WorldLoadedEvent"/>) completes before the telnet
    /// listener begins accepting connections.
    /// <para>
    /// This class is the sole publisher of persistence-lifecycle events
    /// (<see cref="EntityHydratedEvent"/> and <see cref="WorldLoadedEvent"/>).
    /// <c>PersistenceSystem</c> is a pure Core System with no event-bus dependency — it returns
    /// results that this orchestrator translates into events.
    /// </para>
    /// </remarks>
    public sealed class PersistenceBootstrap : IHostedService
    {
        private readonly IPersistenceSystem _persistence;
        private readonly IEventBus _eventBus;
        private readonly ILogger<PersistenceBootstrap> _logger;

        public PersistenceBootstrap(
            IPersistenceSystem persistence,
            IEventBus eventBus,
            ILogger<PersistenceBootstrap> logger)
        {
            _persistence = persistence;
            _eventBus = eventBus;
            _logger = logger;
        }

        /// <summary>
        /// Loads all persisted entities, fires <see cref="EntityHydratedEvent"/> per entity,
        /// then publishes <see cref="WorldLoadedEvent"/> once all entities are in-world.
        /// Completes synchronously with respect to the host's startup sequence — the telnet
        /// listener will not accept connections until this returns.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("PersistenceBootstrap: loading persisted entities...");

            // LoadAllAsync is a pure operation — no events. It returns the IDs of every
            // entity that was successfully restored.
            var hydratedIds = await _persistence.LoadAllAsync(cancellationToken);

            // Fire EntityHydratedEvent per entity. Handlers must not query other entities —
            // the world may still be partially loaded within this loop.
            foreach (var entityId in hydratedIds)
                await _eventBus.PublishAsync(new EntityHydratedEvent(entityId));

            // World is fully loaded; safe for cross-entity startup work from here.
            _logger.LogInformation(
                "PersistenceBootstrap: {Count} entity/entities loaded; publishing WorldLoadedEvent.",
                hydratedIds.Count);
            await _eventBus.PublishAsync(new WorldLoadedEvent());
        }

        /// <summary>
        /// Sweeps all <c>PersistentEntity</c>-carrying entities before the process exits,
        /// ensuring no durable state is lost regardless of area occupancy.
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("PersistenceBootstrap: shutdown flush — writing all persistent entities...");
            await _persistence.FlushAllAsync(cancellationToken);
            _logger.LogInformation("PersistenceBootstrap: shutdown flush complete.");
        }
    }
}
