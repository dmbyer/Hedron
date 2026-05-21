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
    /// Concrete implementations (entity-name lookup, inventory lookup, etc.) are deferred to
    /// slice 6. This interface and the parser call-site land in this slice; no live command
    /// registers a non-null resolver until slice 6.
    /// </para>
    /// </summary>
    public interface IArgumentResolver
    {
        /// <summary>
        /// Returns the candidate strings against which the typed token will be prefix-matched,
        /// or <see langword="null"/> if prefix matching does not apply for this invocation
        /// (causes the parser to pass the raw token through unchanged).
        /// </summary>
        IReadOnlyList<string>? GetCandidates(CommandArgumentResolverContext context);
    }
}
