using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>Outcome of <see cref="StartingRoomResolver.Resolve"/>.</summary>
    /// <param name="BlueprintId">The chosen room id (canonical casing from the room set), or <c>null</c> when unresolved.</param>
    /// <param name="Source">Which step of the chain chose it.</param>
    /// <param name="Warnings">Every fallback taken past a declared or configured id, in order.</param>
    public sealed record StartingRoomDecision(
        string? BlueprintId,
        StartingRoomSource Source,
        IReadOnlyList<string> Warnings);

    /// <summary>
    /// Pure starting-room resolution: manifest declaration → <c>World:StartingRoomBlueprintId</c> →
    /// <c>room.void</c> → ordinal-least room id. Case-insensitive. Shared by the loader's live
    /// resolution and the authoring preview, so both follow the same chain.
    /// </summary>
    public static class StartingRoomResolver
    {
        public const string VoidRoomBlueprintId = "room.void";

        /// <param name="manifestId">The manifest's <c>startingRoomId</c>, or <c>null</c> when undeclared.</param>
        /// <param name="configId">The operator's <c>World:StartingRoomBlueprintId</c>, or <c>null</c>.</param>
        /// <param name="roomIds">Blueprint ids of the candidate <b>rooms</b> (no other kinds).</param>
        public static StartingRoomDecision Resolve(
            string? manifestId,
            string? configId,
            IReadOnlyCollection<string> roomIds)
        {
            var rooms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in roomIds)
            {
                if (!string.IsNullOrEmpty(id))
                    rooms.TryAdd(id, id);
            }

            var warnings = new List<string>();

            if (!string.IsNullOrWhiteSpace(manifestId))
            {
                if (rooms.TryGetValue(manifestId, out var declared))
                    return new StartingRoomDecision(declared, StartingRoomSource.Manifest, warnings);
                warnings.Add($"world.yaml startingRoomId '{manifestId}' is not a known room; falling back.");
            }

            if (!string.IsNullOrWhiteSpace(configId))
            {
                if (rooms.TryGetValue(configId, out var configured))
                    return new StartingRoomDecision(configured, StartingRoomSource.Configuration, warnings);
                warnings.Add($"World:StartingRoomBlueprintId '{configId}' is not a known room; falling back.");
            }

            if (rooms.TryGetValue(VoidRoomBlueprintId, out var voidId))
            {
                warnings.Add($"Using fallback starting room '{voidId}'.");
                return new StartingRoomDecision(voidId, StartingRoomSource.VoidFallback, warnings);
            }

            if (rooms.Count > 0)
            {
                var least = rooms.Values
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(id => id, StringComparer.Ordinal)
                    .First();
                warnings.Add($"Using fallback starting room '{least}' (ordinal-least room id).");
                return new StartingRoomDecision(least, StartingRoomSource.AnyRoomFallback, warnings);
            }

            warnings.Add("No rooms are available to resolve a starting room.");
            return new StartingRoomDecision(null, StartingRoomSource.Unresolved, warnings);
        }
    }
}
