using System;

namespace Hedron.Core
{
    public enum Direction
    {
        North,
        South,
        East,
        West,
        Up,
        Down,
    }

    /// <summary>
    /// Pure helper extensions for <see cref="Direction"/>.
    /// </summary>
    public static class DirectionExtensions
    {
        /// <summary>
        /// Returns the canonical inverse of a direction (N↔S, E↔W, Up↔Down).
        /// Total over all six members — throws <see cref="ArgumentOutOfRangeException"/>
        /// only for undefined enum values.
        /// </summary>
        public static Direction Opposite(this Direction direction) => direction switch
        {
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            Direction.East  => Direction.West,
            Direction.West  => Direction.East,
            Direction.Up    => Direction.Down,
            Direction.Down  => Direction.Up,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction."),
        };
    }
}
