using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Pure helpers for <see cref="SpawnRule.Id"/>: the default derived from a blueprint id,
    /// minting a unique id within a room, and the well-formedness check.
    /// </summary>
    public static class SpawnRuleIds
    {
        private static readonly Regex WellFormed = new(
            "^[A-Za-z0-9._-]{1," + SpawnRuleLimits.MaxIdLength + "}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The id a rule carries when none is authored: the blueprint id, deterministically
        /// truncated to its first <c>MaxIdLength − MaxSuffixLength</c> (60) characters so a
        /// mint suffix always fits under the cap.
        /// </summary>
        public static string DefaultFor(string blueprintId)
        {
            const int max = SpawnRuleLimits.MaxIdLength - SpawnRuleLimits.MaxSuffixLength;
            return blueprintId.Length <= max ? blueprintId : blueprintId.Substring(0, max);
        }

        /// <summary>
        /// Mints an id unique (case-insensitive) among <paramref name="existingIds"/>:
        /// <see cref="DefaultFor"/>, then <c>-2</c> … <c>-999</c>.
        /// </summary>
        /// <exception cref="InvalidOperationException">Every candidate is taken.</exception>
        public static string Mint(IEnumerable<string> existingIds, string blueprintId)
        {
            var taken = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
            var root = DefaultFor(blueprintId);
            if (!taken.Contains(root))
                return root;
            for (int n = 2; n <= 999; n++)
            {
                var candidate = $"{root}-{n}";
                if (!taken.Contains(candidate))
                    return candidate;
            }
            throw new InvalidOperationException(
                $"Cannot mint a spawn rule id for '{blueprintId}': '{root}-2' through '{root}-999' are all taken.");
        }

        /// <summary><c>^[A-Za-z0-9._-]{1,64}$</c>.</summary>
        public static bool IsWellFormed(string? id) => id is not null && WellFormed.IsMatch(id);
    }
}
