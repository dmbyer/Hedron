using System;
using System.Collections.Generic;
using Hedron.Core.Modules.Ascension;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World
{
    /// <summary>
    /// An area's region designation: the range of (Tier, Band) cells the area is meant to hold.
    /// Phase 5 authors one tier with a band range (<see cref="AreaDesignationValidation"/> enforces
    /// <c>Min.Tier == Max.Tier</c>); storing the bounds as two <see cref="PowerBand"/> cells keeps a
    /// later cross-tier range a validation-rule deletion, not a reshape (tiered-overworld R2).
    /// </summary>
    /// <remarks>
    /// Advisory world-content data: consumed by the out-of-designation check
    /// (<see cref="Systems.AreaPlacementCheck"/>), never by gameplay this slice.
    /// </remarks>
    public sealed record AreaDesignation(PowerBand Min, PowerBand Max)
    {
        /// <summary>Lowest authorable band. Same bounds as the mob/item <c>Band</c> tag (1-3 banded).</summary>
        public const int MinBand = 1;

        /// <summary>Highest authorable band.</summary>
        public const int MaxBand = 3;

        /// <summary>Builds a single-tier designation. Performs no validation — see <see cref="AreaDesignationValidation"/>.</summary>
        public static AreaDesignation Of(int tier, int bandMin, int bandMax)
            => new(new PowerBand(tier, bandMin), new PowerBand(tier, bandMax));

        /// <summary>
        /// True when <paramref name="cell"/> lies within <c>Min..Max</c> under (Tier, Band)
        /// lexicographic order — the same total order <c>PowerBudgetTunables.GlobalBandIndex</c>
        /// induces for in-range bands.
        /// </summary>
        public bool Contains(PowerBand cell) => Compare(Min, cell) <= 0 && Compare(cell, Max) <= 0;

        /// <summary>
        /// Parses the admin grammar <c>&lt;tier&gt; &lt;band&gt;</c> or
        /// <c>&lt;tier&gt; &lt;band&gt;-&lt;band&gt;</c>. Parsing only: a parsed value may still fail
        /// <see cref="AreaDesignationValidation"/>.
        /// </summary>
        public static bool TryParse(string? text, out AreaDesignation? designation, out string? error)
        {
            designation = null;
            error = null;

            var parts = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                error = "Expected '<tier> <band>' or '<tier> <band>-<band>'.";
                return false;
            }

            if (!int.TryParse(parts[0], out var tier))
            {
                error = $"Tier '{parts[0]}' is not a number.";
                return false;
            }

            var bands = parts[1].Split('-');
            if (bands.Length > 2 || !int.TryParse(bands[0], out var bandMin))
            {
                error = $"Band '{parts[1]}' must be a number or a range like '1-2'.";
                return false;
            }

            var bandMax = bandMin;
            if (bands.Length == 2 && !int.TryParse(bands[1], out bandMax))
            {
                error = $"Band '{parts[1]}' must be a number or a range like '1-2'.";
                return false;
            }

            designation = Of(tier, bandMin, bandMax);
            return true;
        }

        /// <summary>
        /// The admin setter's full grammar: <c>clear</c> / <c>none</c> (no designation, returned as a
        /// <c>null</c> <paramref name="designation"/>), or anything <see cref="TryParse"/> accepts.
        /// </summary>
        public static bool TryParseOrClear(string? text, out AreaDesignation? designation, out string? error)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                designation = null;
                error = null;
                return true;
            }

            return TryParse(trimmed, out designation, out error);
        }

        /// <summary><c>Tier 0, Band 2</c> or <c>Tier 0, Bands 1-2</c> (cross-tier: <c>T0 B3 - T1 B1</c>).</summary>
        public override string ToString()
        {
            if (Min.Tier != Max.Tier)
                return $"T{Min.Tier} B{Min.Band} - T{Max.Tier} B{Max.Band}";
            return Min.Band == Max.Band
                ? $"Tier {Min.Tier}, Band {Min.Band}"
                : $"Tier {Min.Tier}, Bands {Min.Band}-{Max.Band}";
        }

        private static int Compare(PowerBand a, PowerBand b)
            => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : a.Band.CompareTo(b.Band);
    }

    /// <summary>
    /// The single shape check for <see cref="AreaDesignation"/>, shared by the area deserializer
    /// (throws), <c>IContentValidator.Validate</c> (refuses a save) and the area builder (refuses an
    /// admin mutation). Precedent: <c>SpawnRuleValidation</c>.
    /// </summary>
    public static class AreaDesignationValidation
    {
        public static IReadOnlyList<string> Errors(string areaBlueprintId, AreaDesignation designation)
        {
            var errors = new List<string>();
            var subject = $"Area '{areaBlueprintId}': designation";

            if (designation.Min.Tier != designation.Max.Tier)
                errors.Add($"{subject} must stay within one tier (got tiers {designation.Min.Tier} and {designation.Max.Tier}).");

            foreach (var tier in new[] { designation.Min.Tier, designation.Max.Tier })
            {
                if (tier < 0 || tier > AscensionConstants.MaxTier)
                {
                    errors.Add($"{subject} tier {tier} is outside 0..{AscensionConstants.MaxTier}.");
                    break;
                }
            }

            foreach (var band in new[] { designation.Min.Band, designation.Max.Band })
            {
                if (band < AreaDesignation.MinBand || band > AreaDesignation.MaxBand)
                    errors.Add($"{subject} band {band} is outside {AreaDesignation.MinBand}..{AreaDesignation.MaxBand}.");
            }

            if (designation.Min.Band > designation.Max.Band)
                errors.Add($"{subject} bandMin {designation.Min.Band} exceeds bandMax {designation.Max.Band}.");

            return errors;
        }
    }
}
