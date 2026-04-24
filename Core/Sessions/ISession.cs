using System;
using System.Threading.Tasks;

namespace Hedron.Core.Sessions
{
    /// <summary>
    /// A live player connection. Commands and the dispatcher receive a session to read
    /// player state (via <see cref="PlayerEntityId"/>) and to write output back.
    /// </summary>
    /// <remarks>
    /// The concrete telnet implementation lands with the Phase 2 session layer. Id <c>0</c>
    /// on <see cref="PlayerEntityId"/> means the session is not yet bound to a world entity
    /// (e.g. during login name-prompt) — commands that need an in-world player should check.
    /// </remarks>
    public interface ISession
    {
        /// <summary>Stable identity for this connection.</summary>
        Guid SessionId { get; }

        /// <summary>
        /// Entity id of the player body bound to this session, or <c>0</c> if not yet bound.
        /// </summary>
        uint PlayerEntityId { get; }

        /// <summary>Writes a single line of text to the client (newline appended).</summary>
        Task SendLineAsync(string text);
    }
}
