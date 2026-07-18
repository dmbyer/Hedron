using System;
using Hedron.Core;
using Xunit;

namespace Hedron.Tests
{
    /// <summary>
    /// Tier 1 — pure-function tests for <see cref="DirectionExtensions.Offset"/> and
    /// <see cref="DirectionExtensions.FromOffset"/> (world-editor-grid Postcondition 10).
    ///
    /// Coverage: totality over the six directions, Offset(d.Opposite()) == -Offset(d),
    /// FromOffset is the inverse of Offset for unit offsets, and null for anything else.
    /// </summary>
    public sealed class DirectionExtensionsTests
    {
        public static readonly Direction[] AllDirections =
        {
            Direction.North, Direction.South, Direction.East,
            Direction.West, Direction.Up, Direction.Down,
        };

        [Theory]
        [MemberData(nameof(AllDirectionsData))]
        public void Offset_is_total_over_all_six_directions(Direction direction)
        {
            var offset = direction.Offset();
            // Total means it doesn't throw; every unit offset has exactly one non-zero axis.
            var nonZero = (offset.Dx != 0 ? 1 : 0) + (offset.Dy != 0 ? 1 : 0) + (offset.Dz != 0 ? 1 : 0);
            Assert.Equal(1, nonZero);
        }

        [Theory]
        [MemberData(nameof(AllDirectionsData))]
        public void Offset_of_opposite_is_negation(Direction direction)
        {
            var offset = direction.Offset();
            var oppositeOffset = direction.Opposite().Offset();

            Assert.Equal(-offset.Dx, oppositeOffset.Dx);
            Assert.Equal(-offset.Dy, oppositeOffset.Dy);
            Assert.Equal(-offset.Dz, oppositeOffset.Dz);
        }

        [Theory]
        [MemberData(nameof(AllDirectionsData))]
        public void FromOffset_inverts_Offset_for_unit_offsets(Direction direction)
        {
            var offset = direction.Offset();
            var result = DirectionExtensions.FromOffset(offset.Dx, offset.Dy, offset.Dz);

            Assert.Equal(direction, result);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(1, 1, 0)]
        [InlineData(2, 0, 0)]
        [InlineData(0, 0, 2)]
        [InlineData(1, 0, 1)]
        public void FromOffset_returns_null_for_non_unit_offsets(int dx, int dy, int dz)
        {
            Assert.Null(DirectionExtensions.FromOffset(dx, dy, dz));
        }

        [Fact]
        public void Offset_throws_for_undefined_enum_value()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ((Direction)999).Offset());
        }

        public static TheoryData<Direction> AllDirectionsData()
        {
            var data = new TheoryData<Direction>();
            foreach (var d in AllDirections)
                data.Add(d);
            return data;
        }
    }
}
