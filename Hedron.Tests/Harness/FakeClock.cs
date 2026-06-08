using System;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Harness
{
    /// <summary>
    /// Deterministic <see cref="IClock"/> for tests. Set <see cref="UtcNow"/> directly or call
    /// <see cref="Advance"/> to step time forward (INV-26).
    /// </summary>
    public sealed class FakeClock : IClock
    {
        private DateTime _utcNow;

        public FakeClock(DateTime utcNow)
        {
            _utcNow = utcNow;
        }

        public FakeClock() : this(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)) { }

        public DateTime UtcNow
        {
            get => _utcNow;
            set => _utcNow = value;
        }

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }

    public sealed class FakeClockSelfTest
    {
        [Fact]
        public void FakeClock_returns_set_time()
        {
            var expected = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            var clock = new FakeClock(expected);

            Assert.Equal(expected, clock.UtcNow);
        }

        [Fact]
        public void FakeClock_Advance_steps_time_forward()
        {
            var start = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            var clock = new FakeClock(start);

            clock.Advance(TimeSpan.FromMinutes(5));

            Assert.Equal(start + TimeSpan.FromMinutes(5), clock.UtcNow);
        }
    }
}
