using System;
using System.Collections.Generic;
using Hedron.Core.Systems;

namespace Hedron.Tests.Harness
{
    /// <summary>
    /// An <see cref="IRandom"/> that records the exact <b>sequence</b> of draws made against it.
    ///
    /// <para>
    /// This exists for the INV-26 draw contract: the balance sandbox shares one seeded
    /// <c>IRandom</c> across every system in a run, so a single extra draw shifts the whole stream
    /// and moves every pinned golden. Asserting "the goldens did not move" only proves that
    /// indirectly and after the fact; <see cref="Draws"/> lets a test assert the contract
    /// <i>directly</i>, at the point the contract is made.
    /// </para>
    /// </summary>
    public sealed class CountingRandom : IRandom
    {
        /// <summary>One recorded draw: which method was called, with what bounds.</summary>
        public readonly record struct Draw(string Method, int MinInclusive, int MaxExclusive);

        private readonly Queue<int> _ints;
        private readonly Queue<double> _doubles;

        /// <summary>Every draw made, in order.</summary>
        public List<Draw> Draws { get; } = new();

        public CountingRandom(IEnumerable<int>? ints = null, IEnumerable<double>? doubles = null)
        {
            _ints = new Queue<int>(ints ?? Array.Empty<int>());
            _doubles = new Queue<double>(doubles ?? Array.Empty<double>());
        }

        public int Next(int maxExclusive) => Next(0, maxExclusive);

        public int Next(int minInclusive, int maxExclusive)
        {
            Draws.Add(new Draw(nameof(Next), minInclusive, maxExclusive));
            if (_ints.Count == 0)
                throw new InvalidOperationException("CountingRandom: no more prescribed int values.");

            return _ints.Dequeue();
        }

        public double NextDouble()
        {
            Draws.Add(new Draw(nameof(NextDouble), 0, 0));
            if (_doubles.Count == 0)
                throw new InvalidOperationException("CountingRandom: no more prescribed double values.");

            return _doubles.Dequeue();
        }
    }
}
