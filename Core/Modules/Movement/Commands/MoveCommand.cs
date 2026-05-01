using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Events;
using Hedron.Core.Modules.Movement.Events;
using Hedron.Core.Modules.Movement.Systems;
using Hedron.Core.Sessions;

namespace Hedron.Core.Modules.Movement.Commands
{
    /// <summary>
    /// Handles one cardinal direction. Six instances are registered (one per direction),
    /// each with its own primary verb and short alias.
    /// </summary>
    public class MoveCommand : ICommand
    {
        private readonly Direction _direction;
        private readonly IMovementSystem _movementSystem;
        private readonly IEventBus _eventBus;

        public string Name { get; }
        public IReadOnlyList<string> Aliases { get; }

        public MoveCommand(Direction direction, IMovementSystem movementSystem, IEventBus eventBus)
        {
            _direction = direction;
            _movementSystem = movementSystem;
            _eventBus = eventBus;
            Name = direction.ToString().ToLower();
            Aliases = ShortAlias(direction);
        }

        public async Task ExecuteAsync(ISession session, string arguments)
        {
            var result = _movementSystem.TryMove(session.PlayerEntityId, _direction);
            if (!result.Success)
            {
                await session.SendLineAsync(result.ErrorMessage ?? "You cannot go that way.")
                    .ConfigureAwait(false);
                return;
            }

            await _eventBus.PublishAsync(new PlayerMovedEvent(
                session.PlayerEntityId,
                result.FromRoomEntityId,
                result.ToRoomEntityId,
                _direction)).ConfigureAwait(false);
        }

        private static IReadOnlyList<string> ShortAlias(Direction d) => d switch
        {
            Direction.North => new[] { "n" },
            Direction.South => new[] { "s" },
            Direction.East  => new[] { "e" },
            Direction.West  => new[] { "w" },
            Direction.Up    => new[] { "u" },
            Direction.Down  => new[] { "d" },
            _               => System.Array.Empty<string>(),
        };
    }
}
