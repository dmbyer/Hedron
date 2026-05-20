using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands.Authorization;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// A single player-facing verb. Thin — declares its schema and calls a domain
    /// system (or publishes an event) to do the actual work. Argument parsing,
    /// privilege gating, and help text are all structural — no per-command boilerplate.
    /// </summary>
    public interface ICommand
    {
        /// <summary>Primary verb (case-insensitive, e.g. "look", "say", "north").</summary>
        string Name { get; }

        /// <summary>Alternate verbs that route to this command ("n" → "north", etc.).</summary>
        IReadOnlyList<string> Aliases { get; }

        /// <summary>Groups the command for help display and admin-visibility filtering.</summary>
        CommandCategory Category { get; }

        /// <summary>One-line description used in the 'commands' index.</summary>
        string ShortDescription { get; }

        /// <summary>Multi-paragraph body used in 'help &lt;verb&gt;'.</summary>
        string LongDescription { get; }

        /// <summary>Formal argument grammar shown at the end of 'help &lt;verb&gt;'.</summary>
        string Usage { get; }

        /// <summary>
        /// Requirements a caller must satisfy. Empty list = public (no gate).
        /// The dispatcher iterates this and calls <see cref="IAuthorizationChecker"/>
        /// for each — per-command boilerplate privilege checks are no longer needed.
        /// </summary>
        IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; }

        /// <summary>Declarative argument list. The dispatcher parses against this schema.</summary>
        CommandArgumentSchema ArgumentSchema { get; }

        /// <summary>
        /// Runs the command. <paramref name="context"/> carries typed parsed arguments
        /// and the output writer — do not call <c>session.SendLineAsync</c> directly.
        /// </summary>
        Task ExecuteAsync(CommandContext context);
    }
}
