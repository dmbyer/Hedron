using System.Collections.Generic;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// Read-only lookup used by <see cref="CommandDispatcher"/> Phase 3 to route a verb token
    /// to a known Active Skill ability before falling back to "Unknown command".
    /// </summary>
    public interface IAbilityVerbResolver
    {
        /// <summary>
        /// Returns the single known Active Skill ability whose invocation verb (= AbilityId)
        /// prefix-matches <paramref name="verbToken"/>. Returns <c>false</c> on zero or
        /// ambiguous (&gt;1) matches.
        /// </summary>
        bool TryResolve(uint actorEntityId, string verbToken, out string abilityId);

        /// <summary>
        /// Returns the invoker's known Active Skill ability IDs (for 'abilities'/'skills' discovery).
        /// </summary>
        IReadOnlyList<string> GetInvocableVerbs(uint actorEntityId);
    }
}
