using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Systems;
using Hedron.Core.Modules.Session.Events;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;
using Microsoft.Extensions.Configuration;

namespace Hedron.Server.Sessions
{
    internal sealed class TelnetSession : ISession, IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly ICommandDispatcher _dispatcher;
        private readonly IEventBus _eventBus;
        private readonly ISessionManager _sessionManager;
        private readonly IAccountSystem _accountSystem;
        private readonly IOutputWriterFactory _outputWriterFactory;
        private readonly IPersistenceSystem _persistence;
        private readonly IConfiguration _configuration;

        private string _characterName = string.Empty;

        public Guid SessionId { get; } = Guid.NewGuid();
        public uint PlayerEntityId { get; private set; }
        public string TransportKey => "telnet";

        /// <summary>
        /// Whether ANSI color codes are sent to this client. Defaults from
        /// <c>Output:DefaultColor</c>. Setter seam exists for a future <c>/color off</c> command.
        /// </summary>
        public bool SupportsColor { get; private set; }

        public TelnetSession(
            TcpClient client,
            ICommandDispatcher dispatcher,
            IEventBus eventBus,
            ISessionManager sessionManager,
            IAccountSystem accountSystem,
            IOutputWriterFactory outputWriterFactory,
            IPersistenceSystem persistence,
            IConfiguration configuration,
            bool defaultColor)
        {
            _client = client;
            var stream = client.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            _dispatcher = dispatcher;
            _eventBus = eventBus;
            _sessionManager = sessionManager;
            _accountSystem = accountSystem;
            _outputWriterFactory = outputWriterFactory;
            _persistence = persistence;
            _configuration = configuration;
            SupportsColor = defaultColor;
        }

        public async Task SendLineAsync(string text)
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(text).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                var loginFlow = new LoginFlow(
                    this, _reader, _accountSystem, _outputWriterFactory, _eventBus, _persistence, _configuration);

                var loginResult = await loginFlow.RunAsync(cancellationToken).ConfigureAwait(false);
                if (loginResult is null) return;

                _characterName = loginResult.CharacterName;
                PlayerEntityId = loginResult.CharacterEntityId;

                _sessionManager.Register(this);

                await _eventBus.PublishAsync(new PlayerConnectedEvent(
                    PlayerEntityId, loginResult.CharacterName, loginResult.AccountEntityId))
                    .ConfigureAwait(false);
                await _outputWriterFactory.Create(this).FlushAsync().ConfigureAwait(false);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null) break;
                    await _dispatcher.DispatchAsync(this, line).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (SocketException) { }
            finally
            {
                await HandleDisconnectAsync().ConfigureAwait(false);
            }
        }

        private async Task HandleDisconnectAsync()
        {
            if (PlayerEntityId != 0)
            {
                await _eventBus.PublishAsync(new PlayerDisconnectedEvent(PlayerEntityId, _characterName))
                    .ConfigureAwait(false);
                _sessionManager.Unregister(PlayerEntityId);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
            _reader.Dispose();
            _client.Dispose();
            _writeLock.Dispose();
        }
    }
}
