using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    /// <summary>Resolves the correct <see cref="IOutputFormatter"/> for a given session's transport.</summary>
    public interface IOutputFormatterRegistry
    {
        /// <summary>Returns the formatter whose <see cref="IOutputFormatter.TransportKey"/> matches
        /// <paramref name="session"/>'s transport. Falls back to the first registered formatter
        /// if no exact match exists (safe for the single-transport phase).</summary>
        IOutputFormatter Resolve(ISession session);
    }
}
