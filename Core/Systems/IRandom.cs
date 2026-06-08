namespace Hedron.Core.Systems
{
    /// <summary>
    /// Injectable randomness seam — the single source of non-determinism for game logic.
    /// Systems take <see cref="IRandom"/> by constructor injection instead of reaching for
    /// <c>Random.Shared</c>, so chance-based outcomes can be made deterministic in tests by
    /// substituting a fake (INV-26). Production wiring binds <see cref="SystemRandom"/>.
    /// </summary>
    /// <remarks>
    /// Method semantics mirror <see cref="System.Random"/> exactly so call sites can move to the
    /// seam without changing behaviour. Richer helpers (dice notation, weighted choice) layer on
    /// additively as consumers need them.
    /// </remarks>
    public interface IRandom
    {
        /// <summary>Returns a non-negative random integer less than <paramref name="maxExclusive"/>.</summary>
        int Next(int maxExclusive);

        /// <summary>
        /// Returns a random integer in the range [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).
        /// Mirrors <see cref="System.Random.Next(int,int)"/>.
        /// </summary>
        int Next(int minInclusive, int maxExclusive);

        /// <summary>Returns a random double in [0.0, 1.0). Use for probability rolls.</summary>
        double NextDouble();
    }
}
