using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    /// <summary>
    /// Renders a typed <see cref="IOutputMessage"/> to a transport-encoded string.
    /// One implementation per transport (telnet ANSI, future SignalR/HTML).
    /// </summary>
    public interface IOutputFormatter
    {
        /// <summary>Transport discriminator — "telnet", "signalr", etc.</summary>
        string TransportKey { get; }

        /// <summary>
        /// Render <paramref name="message"/> for <paramref name="session"/>'s transport and
        /// capability flags (e.g. <see cref="ISession.SupportsColor"/>).
        /// </summary>
        string Format(IOutputMessage message, ISession session);
    }
}
