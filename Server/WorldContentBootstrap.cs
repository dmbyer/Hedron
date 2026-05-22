using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Modules.World.Systems;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hedron.Server
{
    /// <summary>
    /// Hosted service that drives the initial content load and entity-spawn pass at startup.
    /// </summary>
    /// <remarks>
    /// <b>Registration order matters.</b> Must be registered after <c>PersistenceBootstrap</c>
    /// (so the spawn pass sees every hydrated entity) and before <c>TelnetServer</c> (so the
    /// world is fully assembled before the listener accepts connections).
    /// </remarks>
    public sealed class WorldContentBootstrap : IHostedService
    {
        private readonly IWorldContentLoader _loader;
        private readonly IEventBus _eventBus;
        private readonly ILogger<WorldContentBootstrap> _logger;

        public WorldContentBootstrap(
            IWorldContentLoader loader,
            IEventBus eventBus,
            ILogger<WorldContentBootstrap> logger)
        {
            _loader = loader;
            _eventBus = eventBus;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("WorldContentBootstrap: loading authored content...");
            await _loader.LoadAndSpawnAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("WorldContentBootstrap: content load complete.");
            await _eventBus.PublishAsync(new WorldContentReadyEvent()).ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
