namespace Hedron.Core.Systems
{
    /// <summary>
    /// Injectable time seam — the single source of wall-clock time for game logic.
    /// Systems take <see cref="IClock"/> by constructor injection instead of reaching for
    /// <c>DateTime.UtcNow</c>, so time-dependent outcomes can be made deterministic in tests by
    /// substituting a fake (INV-26). Production wiring binds <see cref="SystemClock"/>.
    /// </summary>
    public interface IClock
    {
        /// <summary>Returns the current UTC time.</summary>
        DateTime UtcNow { get; }
    }
}
