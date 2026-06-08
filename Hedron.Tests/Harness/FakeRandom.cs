using System;
using System.Collections.Generic;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Harness
{
    /// <summary>
    /// Deterministic <see cref="IRandom"/> for unit tests.
    /// <list type="bullet">
    ///   <item><c>FakeRandom(params int[])</c> — dequeues prescribed ints from <c>Next</c>.</item>
    ///   <item><c>FakeRandom(int seed)</c> — falls back to a seeded <see cref="Random"/>.</item>
    /// </list>
    /// A separate <see cref="Queue{T}"/> of <see cref="double"/> backs <see cref="NextDouble"/>.
    /// </summary>
    public sealed class FakeRandom : IRandom
    {
        private readonly Queue<int>? _intQueue;
        private readonly Queue<double> _doubleQueue = new();
        private readonly Random? _seeded;

        public FakeRandom(params int[] nextValues)
        {
            _intQueue = new Queue<int>(nextValues);
        }

        public FakeRandom(int seed)
        {
            _seeded = new Random(seed);
        }

        /// <summary>Enqueues values that will be returned by <see cref="NextDouble"/> in order.</summary>
        public void EnqueueDouble(double value) => _doubleQueue.Enqueue(value);

        public int Next(int maxExclusive)
            => Next(0, maxExclusive);

        public int Next(int minInclusive, int maxExclusive)
        {
            int value;
            if (_intQueue is not null)
            {
                if (_intQueue.Count == 0)
                    throw new InvalidOperationException(
                        "FakeRandom: no more prescribed int values — enqueue more.");
                value = _intQueue.Dequeue();
            }
            else
            {
                value = _seeded!.Next(minInclusive, maxExclusive);
                return value;
            }

            if (value < minInclusive || value >= maxExclusive)
                throw new InvalidOperationException(
                    $"FakeRandom: prescribed value {value} is outside [{minInclusive}, {maxExclusive}).");

            return value;
        }

        public double NextDouble()
        {
            if (_doubleQueue.Count > 0) return _doubleQueue.Dequeue();
            return _seeded?.NextDouble() ?? throw new InvalidOperationException(
                "FakeRandom: no prescribed double values and no seeded fallback.");
        }
    }

    // ── Self-test ────────────────────────────────────────────────────────────────

    public sealed class FakeRandomTests
    {
        [Fact]
        public void Prescribed_ints_dequeue_in_order_within_range()
        {
            var rng = new FakeRandom(20, 4);
            Assert.Equal(20, rng.Next(1, 21));
            Assert.Equal(4, rng.Next(1, 10));
        }

        [Fact]
        public void Next_maxExclusive_overload_delegates_to_range_overload()
        {
            var rng = new FakeRandom(new int[] { 5 });
            Assert.Equal(5, rng.Next(6));
        }

        [Fact]
        public void Prescribed_value_out_of_range_throws()
        {
            // Use the params int[] ctor explicitly to avoid resolving to the seed ctor.
            var rng = new FakeRandom(new int[] { 99 });
            Assert.Throws<InvalidOperationException>(() => rng.Next(1, 10));
        }

        [Fact]
        public void Seeded_ctor_uses_deterministic_Random()
        {
            var rng1 = new FakeRandom(42);
            var rng2 = new FakeRandom(42);
            var v1 = rng1.Next(1, 100);
            var v2 = rng2.Next(1, 100);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void EnqueueDouble_dequeues_in_order()
        {
            var rng = new FakeRandom(42);
            rng.EnqueueDouble(0.25);
            rng.EnqueueDouble(0.75);
            Assert.Equal(0.25, rng.NextDouble());
            Assert.Equal(0.75, rng.NextDouble());
        }
    }
}
