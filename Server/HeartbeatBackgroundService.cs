using System;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.Time.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hedron.Server
{
    /// <summary>
    /// Initiator that drives the shared game clock by publishing <see cref="HeartbeatTickEvent"/>
    /// at a configurable interval via a <see cref="PeriodicTimer"/>.
    /// </summary>
    /// <remarks>
    /// Interval is read from <c>IConfiguration["Heartbeat:IntervalMs"]</c>; default is 2000 ms.
    /// Registered last in the hosted-service queue so the first tick cannot land before the world
    /// is fully seeded and the telnet listener is open.
    /// </remarks>
    public sealed class HeartbeatBackgroundService : BackgroundService
    {
        private readonly IEventBus _eventBus;
        private readonly ILogger<HeartbeatBackgroundService> _logger;
        private readonly TimeSpan _interval;

        public HeartbeatBackgroundService(
            IEventBus eventBus,
            IConfiguration configuration,
            ILogger<HeartbeatBackgroundService> logger)
        {
            _eventBus = eventBus;
            _logger = logger;

            var ms = configuration.GetValue<int>("Heartbeat:IntervalMs", defaultValue: 2000);
            _interval = TimeSpan.FromMilliseconds(ms);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "HeartbeatBackgroundService: started; interval = {Interval}.",
                _interval);

            var lastTimestamp = DateTimeOffset.UtcNow;
            long tickId = 0;
            using var timer = new PeriodicTimer(_interval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    var now = DateTimeOffset.UtcNow;
                    var elapsed = now - lastTimestamp;
                    lastTimestamp = now;
                    tickId++;

                    try
                    {
                        await _eventBus.PublishAsync(new HeartbeatTickEvent(tickId, now, elapsed));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "HeartbeatBackgroundService: unhandled exception dispatching tick {TickId}.",
                            tickId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown — host cancelled stoppingToken.
            }

            _logger.LogInformation("HeartbeatBackgroundService: stopped.");
        }
    }
}
