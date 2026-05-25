using System.Collections.Generic;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// Resolves a typed argument token against a dynamic candidate list, enabling prefix
    /// matching on game-world strings (entity names, inventory items, room exits, etc.).
    /// <para>
    /// Return <see langword="null"/> from <see cref="GetCandidates"/> if prefix matching does
    /// not apply for this invocation — the parser will pass the raw token through unchanged.
    /// </para>
    /// <para>
    /// Each <see cref="ResolvedCandidate"/> pairs a match string (name or keyword alias) with
    /// the canonical value that is substituted into the parsed argument on a win. The parser
    /// deduplicates by <see cref="ResolvedCandidate.CanonicalValue"/> after prefix matching, so
    /// multiple keyword aliases that map to the same item are not treated as ambiguous.
    /// </para>
    /// </summary>
    public interface IArgumentResolver
    {
        /// <summary>
        /// Returns the candidate list against which the typed token will be prefix-matched,
        /// or <see langword="null"/> if prefix matching does not apply for this invocation
        /// (causes the parser to pass the raw token through unchanged).
        /// </summary>
        IReadOnlyList<ResolvedCandidate>? GetCandidates(CommandArgumentResolverContext context);
    }
}
