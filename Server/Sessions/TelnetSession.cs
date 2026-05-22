using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Session.Events;
using Hedron.Core.Sessions;

namespace Hedron.Server.Sessions
{
    internal sealed class TelnetSession : ISession, IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly ICommandDispatcher _dispatcher;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly ISessionManager _sessionManager;

        private string _playerName = string.Empty;

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
            EntityService entityService,
            IEventBus eventBus,
            ISessionManager sessionManager,
            bool defaultColor)
        {
            _client = client;
            var stream = client.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            _dispatcher = dispatcher;
            _entityService = entityService;
            _eventBus = eventBus;
            _sessionManager = sessionManager;
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
                var name = await PromptForNameAsync(cancellationToken).ConfigureAwait(false);
                if (name is null) return;

                _playerName = name;
                var entity = _entityService.CreateEntity();
                PlayerEntityId = entity.Id;
                _sessionManager.Register(this);

                await _eventBus.PublishAsync(new PlayerConnectedEvent(PlayerEntityId, _playerName))
                    .ConfigureAwait(false);

                await SendLineAsync($"Welcome, {_playerName}!").ConfigureAwait(false);

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

        private async Task<string?> PromptForNameAsync(CancellationToken cancellationToken)
        {
            try
            {
                await SendLineAsync("What is your name?").ConfigureAwait(false);
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null) return null;
                    var name = line.Trim();
                    if (name.Length > 0) return name;
                    await SendLineAsync("Please enter a name.").ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (SocketException) { }
            return null;
        }

        private async Task HandleDisconnectAsync()
        {
            if (PlayerEntityId != 0)
            {
                _sessionManager.Unregister(PlayerEntityId);
                await _eventBus.PublishAsync(new PlayerDisconnectedEvent(PlayerEntityId, _playerName))
                    .ConfigureAwait(false);
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
