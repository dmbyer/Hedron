using Hedron.Core.Modules.Persistence;
using Hedron.Core.Systems;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hedron.Server
{
    /// <summary>
    /// Background service that periodically flushes all persistent entities to SQLite.
    /// On each tick, calls <c>IPersistenceSystem.FlushDirtyAsync</c>, which writes every
    /// <c>PersistentEntity</c>-carrying entity in the world. No footprint calculation is
    /// performed — the flush pool is small enough that a full sweep is always cheap.
    /// </summary>
    /// <remarks>
    /// Interval is read from <see cref="PersistenceOptions.FlushIntervalSeconds"/>; default
    /// is 60 seconds.  Override via environment variable
    /// <c>HEDRON_Persistence__FlushIntervalSeconds=5</c> for fast dev-iteration.
    /// </remarks>
    public sealed class PersistenceFlushTimer : BackgroundService
    {
        private readonly IPersistenceSystem _persistence;
        private readonly ILogger<PersistenceFlushTimer> _logger;
        private readonly TimeSpan _interval;

        public PersistenceFlushTimer(
            IPersistenceSystem persistence,
            IOptions<PersistenceOptions> options,
            ILogger<PersistenceFlushTimer> logger)
        {
            _persistence = persistence;
            _logger = logger;
            _interval = TimeSpan.FromSeconds(options.Value.FlushIntervalSeconds);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "PersistenceFlushTimer: started; flush interval = {Interval}.",
                _interval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, stoppingToken);
                    await _persistence.FlushDirtyAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PersistenceFlushTimer: unexpected error during periodic flush.");
                }
            }

            _logger.LogInformation("PersistenceFlushTimer: stopped.");
        }
    }
}
