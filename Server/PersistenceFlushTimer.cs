using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hedron.Server
{
    /// <summary>
    /// Background service that periodically flushes the active player footprint.
    /// Collects the room IDs occupied by at least one connected player and calls
    /// <c>IPersistenceSystem.FlushActivePlayerFootprintAsync</c>, which writes every
    /// <c>PersistentEntity</c>-carrying entity located in those rooms.
    /// </summary>
    /// <remarks>
    /// Interval is read from <c>IConfiguration["Persistence:FlushIntervalSeconds"]</c>; default
    /// is 60 seconds.  Configure in <c>appsettings.json</c> or via environment variable
    /// (e.g. <c>Persistence__FlushIntervalSeconds=5</c> for fast dev-iteration).
    /// </remarks>
    public sealed class PersistenceFlushTimer : BackgroundService
    {
        private readonly IPersistenceSystem _persistence;
        private readonly ISessionManager _sessionManager;
        private readonly Hedron.Core.ECS.EntityService _entityService;
        private readonly ILogger<PersistenceFlushTimer> _logger;
        private readonly TimeSpan _interval;

        public PersistenceFlushTimer(
            IPersistenceSystem persistence,
            ISessionManager sessionManager,
            Hedron.Core.ECS.EntityService entityService,
            IConfiguration configuration,
            ILogger<PersistenceFlushTimer> logger)
        {
            _persistence = persistence;
            _sessionManager = sessionManager;
            _entityService = entityService;
            _logger = logger;

            var seconds = configuration.GetValue<int>("Persistence:FlushIntervalSeconds", defaultValue: 60);
            _interval = TimeSpan.FromSeconds(seconds);
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
                    await FlushFootprintAsync(stoppingToken);
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

        private async Task FlushFootprintAsync(CancellationToken ct)
        {
            var sessions = _sessionManager.GetAll();
            if (sessions.Count == 0) return;

            var occupiedRooms = new HashSet<uint>();
            foreach (var session in sessions)
            {
                if (session.PlayerEntityId == 0) continue;
                if (_entityService.TryGet<LocationComponent>(session.PlayerEntityId, out var loc))
                    occupiedRooms.Add(loc.RoomEntityId);
            }

            await _persistence.FlushActivePlayerFootprintAsync(occupiedRooms, ct);
        }
    }
}
