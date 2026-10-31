using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// One out-of-designation placement: a room spawn rule placing a banded mob or item whose
    /// authored (Tier, Band) falls outside its room's area designation (R9).
    /// </summary>
    public sealed record PlacementFinding(
        string RoomBlueprintId,
        string RuleId,
        string BlueprintId,
        PlacedKind Kind,
        string AreaBlueprintId,
        PowerBand Authored,
        AreaDesignation Designation)
    {
        /// <summary>The single R9 one-liner every surface (boot log, <c>area</c>, editors, Integrity) shows.</summary>
        public string Describe() =>
            $"Room '{RoomBlueprintId}' rule '{RuleId}' places {Kind.ToString().ToLowerInvariant()} " +
            $"'{BlueprintId}' (Tier {Authored.Tier}, Band {Authored.Band}) outside area " +
            $"'{AreaBlueprintId}' designation ({Designation}).";
    }

    /// <summary>
    /// The out-of-designation check: a pure judgement over authored tags (never the power oracle —
    /// band drift is <c>IBalanceAuditSystem</c>'s job, R10), plus a sweep over a content source.
    /// Every surface calls this, so no two can disagree (INV-19). Advisory: findings are warnings,
    /// never errors (R11).
    /// </summary>
    public static class AreaPlacementCheck
    {
        /// <summary>
        /// A finding only when the room has an area, the area is designated, the blueprint is a
        /// known banded mob or item, and its authored cell is outside the designation; otherwise <c>null</c>.
        /// </summary>
        public static PlacementFinding? Judge(PlacementSite site, IAreaPlacementSource source)
        {
            if (string.IsNullOrEmpty(site.AreaBlueprintId))
                return null;

            var designation = source.DesignationOf(site.AreaBlueprintId);
            if (designation is null)
                return null;

            if (source.TagOf(site.Rule.BlueprintId) is not { } tag || tag.Band == 0)
                return null;

            var authored = new PowerBand(tag.Tier, tag.Band);
            if (designation.Contains(authored))
                return null;

            return new PlacementFinding(
                site.RoomBlueprintId, site.Rule.Id, site.Rule.BlueprintId, tag.Kind,
                site.AreaBlueprintId, authored, designation);
        }

        /// <summary>
        /// One finding per offending (room, rule), ordered by room blueprint id then rule id
        /// (ordinal). <paramref name="areaBlueprintId"/> restricts to rooms in that area
        /// (case-insensitive).
        /// </summary>
        public static IReadOnlyList<PlacementFinding> Sweep(IAreaPlacementSource source, string? areaBlueprintId = null)
        {
            var findings = new List<PlacementFinding>();
            foreach (var site in source.Sites())
            {
                if (areaBlueprintId is not null &&
                    // Case-insensitive, like the template registry that resolves area ids.
                    !string.Equals(site.AreaBlueprintId, areaBlueprintId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Judge(site, source) is { } finding)
                    findings.Add(finding);
            }

            return findings
                .OrderBy(f => f.RoomBlueprintId, StringComparer.Ordinal)
                .ThenBy(f => f.RuleId, StringComparer.Ordinal)
                .ToList();
        }
    }
}
