using System;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Deterministic <see cref="IRandom"/> backed by <see cref="Random"/> constructed from a fixed
    /// seed. Unlike <see cref="SystemRandom"/> (which wraps the shared, non-deterministic instance),
    /// a <see cref="SeededRandom"/> replays the same sequence for the same seed — the determinism
    /// source for bulk content generation (INV-26).
    /// </summary>
    /// <remarks>
    /// Constructed per run (not a DI singleton): the bulk generator builds one from
    /// <c>GenerationProfile.Seed</c> instead of consuming the ambient <see cref="SystemRandom"/>
    /// singleton. Reproducibility is within a runtime/CI image — <see cref="Random"/>'s sequence is
    /// not guaranteed stable across .NET versions; a stable PRNG can replace it behind this same
    /// seam later without reshaping callers.
    /// </remarks>
    public sealed class SeededRandom : IRandom
    {
        private readonly Random _random;

        public SeededRandom(int seed)
        {
            _random = new Random(seed);
        }

        public int Next(int maxExclusive) => _random.Next(maxExclusive);

        public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

        public double NextDouble() => _random.NextDouble();
    }
}
