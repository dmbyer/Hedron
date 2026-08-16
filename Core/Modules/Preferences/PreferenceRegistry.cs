using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Modules.Preferences
{
    /// <summary>
    /// One player-configurable setting's metadata: the name the player types, the default, and the
    /// one-line description <c>config</c> lists.
    /// </summary>
    public sealed record PreferenceDefinition(
        PreferenceId Id,
        string Name,
        bool DefaultValue,
        string Description);

    /// <summary>
    /// The shipped preference catalog (configuration Category 3 — compiled rows). Adding a
    /// preference is a row here plus an enum member; the <c>config</c> command, persistence, and
    /// the default-fallback all pick it up with no further wiring.
    /// </summary>
    public static class PreferenceRegistry
    {
        /// <summary>Every registered preference, in display order.</summary>
        public static readonly IReadOnlyList<PreferenceDefinition> All = new[]
        {
            new PreferenceDefinition(
                PreferenceId.ProgressionXpMessages,
                "progressionxp",
                DefaultValue: true,
                "Show a line whenever you gain experience."),

            new PreferenceDefinition(
                PreferenceId.ProgressionImprovementMessages,
                "progressionimprove",
                DefaultValue: true,
                "Show a line whenever an attribute or ability improves."),
        };

        private static readonly Dictionary<PreferenceId, PreferenceDefinition> ById =
            All.ToDictionary(d => d.Id);

        /// <summary>The shipped default for <paramref name="id"/> — what an unset preference reads as.</summary>
        public static bool DefaultFor(PreferenceId id)
            => ById.TryGetValue(id, out var def) && def.DefaultValue;

        /// <summary>Metadata for <paramref name="id"/>.</summary>
        public static PreferenceDefinition Get(PreferenceId id) => ById[id];

        /// <summary>
        /// Resolves the name a player typed. Matches the full name case-insensitively first, then
        /// falls back to an unambiguous prefix so <c>config progressionxp</c> can be shortened.
        /// </summary>
        public static bool TryResolve(string? name, out PreferenceId id)
        {
            id = default;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var trimmed = name.Trim();

            foreach (var def in All)
            {
                if (string.Equals(def.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    id = def.Id;
                    return true;
                }
            }

            var prefixMatches = All
                .Where(def => def.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (prefixMatches.Count != 1)
                return false;

            id = prefixMatches[0].Id;
            return true;
        }
    }
}
