using System;
using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// The single rule-shape check for a room's spawn rules. The room deserializer throws on
    /// these errors, <c>ContentValidator.Validate(RoomTemplate)</c> refuses a save with them, and
    /// <c>RoomBuilderSystem</c> refuses an admin mutation with them. Reference checks (does the
    /// blueprint exist?) are not shape and stay warnings elsewhere.
    /// </summary>
    public static class SpawnRuleValidation
    {
        public static IReadOnlyList<string> Errors(string roomBlueprintId, IReadOnlyList<SpawnRule> rules)
        {
            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in rules)
            {
                var label = string.IsNullOrWhiteSpace(rule.BlueprintId) ? "(blank)" : rule.BlueprintId;

                if (string.IsNullOrWhiteSpace(rule.BlueprintId))
                    errors.Add($"Room '{roomBlueprintId}': spawn rule '{rule.Id}' has a blank 'blueprintId'.");

                if (!SpawnRuleIds.IsWellFormed(rule.Id))
                    errors.Add(
                        $"Room '{roomBlueprintId}': spawn rule for '{label}' has malformed 'id' '{rule.Id}' " +
                        $"(1-{SpawnRuleLimits.MaxIdLength} characters of A-Z, a-z, 0-9, '.', '_', '-').");
                else if (!seen.Add(rule.Id))
                    errors.Add(
                        $"Room '{roomBlueprintId}': spawn rule for '{label}' has duplicate 'id' '{rule.Id}' " +
                        "- give each rule that shares a blueprint an explicit, distinct 'id'.");

                if (rule.Count < 1 || rule.Count > SpawnRuleLimits.MaxCountPerRule)
                    errors.Add(
                        $"Room '{roomBlueprintId}': spawn rule '{rule.Id}' for '{label}' has 'count' {rule.Count} " +
                        $"(must be 1-{SpawnRuleLimits.MaxCountPerRule}).");

                if (rule.RespawnDelaySeconds < 0)
                    errors.Add(
                        $"Room '{roomBlueprintId}': spawn rule '{rule.Id}' for '{label}' has negative " +
                        $"'respawnDelaySeconds' {rule.RespawnDelaySeconds}.");
            }

            return errors;
        }
    }
}
