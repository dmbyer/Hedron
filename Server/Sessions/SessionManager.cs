using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Sessions;

namespace Hedron.Server.Sessions
{
    internal sealed class SessionManager : ISessionManager
    {
        private readonly ConcurrentDictionary<uint, ISession> _sessions = new();

        public void Register(ISession session) =>
            _sessions[session.PlayerEntityId] = session;

        public void Unregister(uint playerEntityId) =>
            _sessions.TryRemove(playerEntityId, out _);

        public ISession? GetSession(uint playerEntityId) =>
            _sessions.TryGetValue(playerEntityId, out var session) ? session : null;

        public IReadOnlyCollection<ISession> GetAll() =>
            _sessions.Values.ToList();
    }
}
