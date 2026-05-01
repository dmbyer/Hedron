namespace Hedron.Core.Modules.Movement.Systems
{
    public interface IMovementSystem
    {
        /// <summary>
        /// Attempts to move <paramref name="playerEntityId"/> one step in <paramref name="direction"/>.
        /// Updates <c>LocationComponent</c> on success. Does not publish events.
        /// </summary>
        MoveResult TryMove(uint playerEntityId, Direction direction);
    }

    public readonly struct MoveResult
    {
        public bool Success { get; }
        public uint FromRoomEntityId { get; }
        public uint ToRoomEntityId { get; }
        public string? ErrorMessage { get; }

        private MoveResult(bool success, uint from, uint to, string? error)
        {
            Success = success;
            FromRoomEntityId = from;
            ToRoomEntityId = to;
            ErrorMessage = error;
        }

        public static MoveResult Moved(uint from, uint to) => new(true, from, to, null);
        public static MoveResult Blocked(string reason) => new(false, 0, 0, reason);
    }
}
