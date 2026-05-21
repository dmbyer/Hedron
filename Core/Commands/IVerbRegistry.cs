using System.Collections.Generic;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// Read-only view of the registered command namespace. Implemented by
    /// <see cref="CommandDispatcher"/> and consumed by prefix resolution,
    /// <c>HelpCommand</c>, and future tab-completion without coupling to the
    /// full dispatcher contract.
    /// </summary>
    public interface IVerbRegistry
    {
        /// <summary>
        /// All registered commands — one entry per command, not per alias.
        /// Order is not guaranteed; sort at the call site.
        /// </summary>
        IReadOnlyCollection<ICommand> AllCommands { get; }

        /// <summary>
        /// Exact-match lookup by primary verb or alias (case-insensitive).
        /// Returns <see langword="false"/> if no match is found.
        /// </summary>
        bool TryGetExact(string verb, out ICommand? command);

        /// <summary>
        /// Returns all <see cref="CommandMatchingMode.Partial"/> commands whose
        /// <see cref="ICommand.Name"/> starts with <paramref name="verb"/>
        /// (case-insensitive), sorted alphabetically by name.
        /// <list type="bullet">
        ///   <item>Empty — no match; caller should write an "unknown command" message.</item>
        ///   <item>One entry — unique prefix; caller may dispatch or display that command.</item>
        ///   <item>Two or more entries — ambiguous; caller should write a disambiguation message.</item>
        /// </list>
        /// This is the single location for the prefix-filter LINQ. Both
        /// <see cref="CommandDispatcher"/> and <c>HelpCommand</c> call this method so
        /// the resolution semantics stay consistent.
        /// </summary>
        IReadOnlyList<ICommand> GetPrefixCandidates(string verb);
    }
}
