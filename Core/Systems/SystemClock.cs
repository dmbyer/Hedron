using System;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Production <see cref="IClock"/> backed by <see cref="DateTime.UtcNow"/>.
    /// Registered as a DI singleton in the composition root. Tests never construct this — they
    /// substitute a deterministic fake (INV-26).
    /// </summary>
    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
