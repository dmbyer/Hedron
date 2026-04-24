using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Sessions;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// A single player-facing verb. Thin — parses its argument string and calls a domain
    /// system (or publishes an event) to do the actual work.
    /// </summary>
    public interface ICommand
    {
        /// <summary>Primary verb (case-insensitive, e.g. "look", "say", "north").</summary>
        string Name { get; }

        /// <summary>Alternate verbs that route to this command ("n" → "north", etc.).</summary>
        IReadOnlyList<string> Aliases { get; }

        /// <summary>
        /// Runs the command against the given session. <paramref name="arguments"/> is the
        /// raw tail of the input line after the verb, with surrounding whitespace trimmed.
        /// </summary>
        Task ExecuteAsync(ISession session, string arguments);
    }
}
