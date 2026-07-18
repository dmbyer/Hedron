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

        /// <summary>
        /// Unit grid offset for a direction, under the authoring-grid convention
        /// East = X+1, North = Y+1, Up = Z+1. Total over all six members — throws
        /// <see cref="ArgumentOutOfRangeException"/> only for undefined enum values.
        /// </summary>
        public static (int Dx, int Dy, int Dz) Offset(this Direction direction) => direction switch
        {
            Direction.North => (0, 1, 0),
            Direction.South => (0, -1, 0),
            Direction.East  => (1, 0, 0),
            Direction.West  => (-1, 0, 0),
            Direction.Up    => (0, 0, 1),
            Direction.Down  => (0, 0, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction."),
        };

        /// <summary>
        /// Inverse of <see cref="Offset"/> for a unit grid offset. Returns <c>null</c> for any
        /// offset that isn't exactly one of the six unit vectors (including the zero vector).
        /// </summary>
        public static Direction? FromOffset(int dx, int dy, int dz) => (dx, dy, dz) switch
        {
            (0, 1, 0)  => Direction.North,
            (0, -1, 0) => Direction.South,
            (1, 0, 0)  => Direction.East,
            (-1, 0, 0) => Direction.West,
            (0, 0, 1)  => Direction.Up,
            (0, 0, -1) => Direction.Down,
            _ => null,
        };
    }
}
