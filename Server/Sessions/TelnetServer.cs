using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Systems;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hedron.Server.Sessions
{
    internal sealed class TelnetServer : BackgroundService
    {
        private readonly ICommandDispatcher _dispatcher;
        private readonly IEventBus _eventBus;
        private readonly ISessionManager _sessionManager;
        private readonly IAccountSystem _accountSystem;
        private readonly IOutputWriterFactory _outputWriterFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TelnetServer> _logger;
        private readonly bool _defaultColor;

        private const int Port = 4000;

        public TelnetServer(
            ICommandDispatcher dispatcher,
            IEventBus eventBus,
            ISessionManager sessionManager,
            IAccountSystem accountSystem,
            IOutputWriterFactory outputWriterFactory,
            IConfiguration configuration,
            ILogger<TelnetServer> logger,
            IOptions<OutputConfiguration> outputConfig)
        {
            _dispatcher = dispatcher;
            _eventBus = eventBus;
            _sessionManager = sessionManager;
            _accountSystem = accountSystem;
            _outputWriterFactory = outputWriterFactory;
            _configuration = configuration;
            _logger = logger;
            _defaultColor = outputConfig.Value.DefaultColor;
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
                client, _dispatcher, _eventBus, _sessionManager,
                _accountSystem, _outputWriterFactory, _configuration, _defaultColor);
            await session.RunAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}
