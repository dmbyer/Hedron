using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    public sealed class SessionBufferRegistry : ISessionBufferRegistry
    {
        private readonly IOutputFormatterRegistry _formatterRegistry;
        private readonly IPromptSource _promptSource;
        private readonly ConcurrentDictionary<Guid, SessionOutputBuffer> _buffers = new();

        public SessionBufferRegistry(IOutputFormatterRegistry formatterRegistry, IPromptSource promptSource)
        {
            _formatterRegistry = formatterRegistry;
            _promptSource = promptSource;
        }

        public ISessionOutputBuffer GetOrCreate(ISession session) =>
            _buffers.GetOrAdd(session.SessionId,
                _ => new SessionOutputBuffer(session, _formatterRegistry, _promptSource));

        public void Release(Guid sessionId) =>
            _buffers.TryRemove(sessionId, out _);

        public async Task FlushAllPendingAsync()
        {
            foreach (var (_, buffer) in _buffers)
            {
                if (buffer.HasPending)
                    await buffer.FlushAsync().ConfigureAwait(false);
            }
        }
    }
}
