using System;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Production <see cref="IRandom"/> backed by the thread-safe <see cref="Random.Shared"/>.
    /// Registered as a DI singleton in the composition root. Tests never construct this — they
    /// substitute a deterministic fake (INV-26).
    /// </summary>
    public sealed class SystemRandom : IRandom
    {
        public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);

        public int Next(int minInclusive, int maxExclusive) => Random.Shared.Next(minInclusive, maxExclusive);

        public double NextDouble() => Random.Shared.NextDouble();
    }
}
