using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hedron.Server.Sessions
{
    internal sealed class TelnetServer : BackgroundService
    {
        private readonly ICommandDispatcher _dispatcher;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly ISessionManager _sessionManager;
        private readonly ILogger<TelnetServer> _logger;

        private const int Port = 4000;

        public TelnetServer(
            ICommandDispatcher dispatcher,
            EntityService entityService,
            IEventBus eventBus,
            ISessionManager sessionManager,
            ILogger<TelnetServer> logger)
        {
            _dispatcher = dispatcher;
            _entityService = entityService;
            _eventBus = eventBus;
            _sessionManager = sessionManager;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            _logger.LogInformation("Telnet listener started on port {Port}", Port);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(stoppingToken).ConfigureAwait(false);
                    _ = HandleClientAsync(client, stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                listener.Stop();
                _logger.LogInformation("Telnet listener stopped");
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            await using var session = new TelnetSession(
                client, _dispatcher, _entityService, _eventBus, _sessionManager);
            await session.RunAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}
