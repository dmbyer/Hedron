using System;
using Hedron.Core.Modules.Time.Events;
using Xunit;

namespace Hedron.Tests.Harness
{
    /// <summary>
    /// Factory for synthetic <see cref="HeartbeatTickEvent"/> instances used in tick-driven tests.
    /// </summary>
    public static class Ticks
    {
        /// <summary>
        /// Creates a <see cref="HeartbeatTickEvent"/> with the given <paramref name="id"/> and
        /// <paramref name="elapsedMs"/>.  <c>Timestamp</c> is set to a stable reference point
        /// (<c>DateTimeOffset.UnixEpoch</c> + <paramref name="id"/> seconds) so tests are deterministic.
        /// </summary>
        public static HeartbeatTickEvent At(long id, double elapsedMs = 2000)
            => new HeartbeatTickEvent(
                TickId: id,
                Timestamp: DateTimeOffset.UnixEpoch.AddSeconds(id),
                Elapsed: TimeSpan.FromMilliseconds(elapsedMs));
    }

    // ── Self-test ────────────────────────────────────────────────────────────────

    public sealed class TicksTests
    {
        [Fact]
        public void At_returns_event_with_correct_TickId_and_Elapsed()
        {
            var tick = Ticks.At(id: 7, elapsedMs: 1500);

            Assert.Equal(7L, tick.TickId);
            Assert.Equal(TimeSpan.FromMilliseconds(1500), tick.Elapsed);
        }

        [Fact]
        public void At_default_elapsed_is_2000ms()
        {
            var tick = Ticks.At(id: 1);
            Assert.Equal(TimeSpan.FromMilliseconds(2000), tick.Elapsed);
        }
    }
}
