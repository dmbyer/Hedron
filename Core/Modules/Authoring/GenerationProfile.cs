using System.Collections.Generic;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// The sole input that determines a bulk-generation run's output. A profile is a pure-data
    /// record loaded from a YAML file (or, eventually, built at runtime for procedural content);
    /// two runs with identical profiles produce byte-identical YAML (within a runtime/CI image —
    /// see <see cref="Seed"/>). Consumed by <see cref="Systems.IContentGenerationSystem.GenerateAsync"/>.
    /// </summary>
    /// <remarks>
    /// Pure data, no logic. Ranges are inclusive <c>(Min, Max)</c> tuples; densities are the mean
    /// count of mobs/items placed per generated room. The generator rolls every choice through an
    /// <see cref="Hedron.Core.Systems.IRandom"/> seeded from <see cref="Seed"/> (INV-26), so the run
    /// is deterministic and reproducible.
    /// </remarks>
    public sealed record GenerationProfile
    {
        /// <summary>RNG seed. A fixed seed makes the whole run reproducible (INV-26).</summary>
        public int Seed { get; init; }

        /// <summary>How many areas to generate.</summary>
        public int AreaCount { get; init; } = 1;

        /// <summary>Inclusive range for the number of rooms generated per area.</summary>
        public (int Min, int Max) RoomsPerArea { get; init; } = (1, 1);

        /// <summary>Inclusive range of mob/area levels generated, used to scale stats.</summary>
        public (int Min, int Max) LevelRange { get; init; } = (1, 1);

        /// <summary>Mean number of mobs placed per generated room.</summary>
        public double MobDensity { get; init; }

        /// <summary>Mean number of items placed per generated room.</summary>
        public double ItemDensity { get; init; }

        /// <summary>
        /// Weighted distribution of elemental affinities assigned to generated areas. Empty leaves
        /// generated areas without an aspect affinity.
        /// </summary>
        public IReadOnlyList<AspectMixEntry> AspectMix { get; init; } = new List<AspectMixEntry>();

        /// <summary>How generated mob stats scale across <see cref="LevelRange"/>.</summary>
        public ScalingCurve Scaling { get; init; } = ScalingCurve.Linear;

        /// <summary>
        /// Prefix for every deterministically derived blueprint id (e.g. <c>gen.</c> →
        /// <c>gen.area.0001</c>). The prefix + a per-run counter replaces the <c>mk*</c> builders'
        /// <c>Guid</c> ids, which is what makes a fixed-seed run byte-reproducible.
        /// </summary>
        public string BlueprintPrefix { get; init; } = "gen.";
    }
}
