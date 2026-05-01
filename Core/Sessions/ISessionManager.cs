using System.Collections.Generic;

namespace Hedron.Core.Sessions
{
    /// <summary>
    /// Tracks all live sessions. Used by <c>BroadcastSystem</c> and other domain systems
    /// to send output to specific players or to all connected players.
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>Adds a session to the registry. Called once the session is bound to a player entity.</summary>
        void Register(ISession session);

        /// <summary>Removes a session from the registry on disconnect.</summary>
        void Unregister(uint playerEntityId);

        /// <summary>Returns the session bound to <paramref name="playerEntityId"/>, or <c>null</c> if not found.</summary>
        ISession? GetSession(uint playerEntityId);

        /// <summary>Returns a snapshot of all currently registered sessions.</summary>
        IReadOnlyCollection<ISession> GetAll();
    }
}
