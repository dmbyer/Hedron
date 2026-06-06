using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    internal sealed class SessionOutputBuffer : ISessionOutputBuffer
    {
        private readonly ISession _session;
        private readonly IOutputFormatterRegistry _registry;
        private readonly IPromptSource _promptSource;
        private readonly List<IOutputMessage> _pending = new();
        private readonly object _lock = new();

        public SessionOutputBuffer(ISession session, IOutputFormatterRegistry registry, IPromptSource promptSource)
        {
            _session = session;
            _registry = registry;
            _promptSource = promptSource;
        }

        public bool HasPending { get { lock (_lock) { return _pending.Count > 0; } } }

        public void Enqueue(IOutputMessage message)
        {
            lock (_lock) { _pending.Add(message); }
        }

        public async Task FlushAsync()
        {
            List<IOutputMessage> snapshot;
            lock (_lock)
            {
                if (_pending.Count == 0)
                {
                    snapshot = new List<IOutputMessage>();
                }
                else
                {
                    snapshot = new List<IOutputMessage>(_pending);
                    _pending.Clear();
                }
            }

            var formatter = _registry.Resolve(_session);
            foreach (var msg in snapshot)
            {
                var rendered = formatter.Format(msg, _session);
                await _session.SendLineAsync(rendered).ConfigureAwait(false);
            }

            var prompt = _promptSource.GetPrompt(_session.PlayerEntityId);
            if (prompt != null)
            {
                if (snapshot.Count > 0)
                    await _session.SendLineAsync(string.Empty).ConfigureAwait(false);
                var rendered = formatter.Format(prompt, _session);
                await _session.SendLineAsync(rendered).ConfigureAwait(false);
            }
        }
    }
}
