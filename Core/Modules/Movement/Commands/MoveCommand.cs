using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Events;
using Hedron.Core.Modules.Movement.Events;
using Hedron.Core.Modules.Movement.Systems;
using Hedron.Core.Output;

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
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription { get; }
        public string LongDescription { get; }
        public string Usage { get; }
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema => CommandArgumentSchema.Empty;

        public MoveCommand(Direction direction, IMovementSystem movementSystem, IEventBus eventBus)
        {
            _direction = direction;
            _movementSystem = movementSystem;
            _eventBus = eventBus;
            Name = direction.ToString().ToLower();
            Aliases = ShortAlias(direction);
            ShortDescription = $"Move {direction.ToString().ToLower()}.";
            LongDescription = $"Move in the {direction.ToString().ToLower()} direction if an exit exists.";
            Usage = direction.ToString().ToLower();
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var result = _movementSystem.TryMove(context.InvokerEntityId, _direction);
            if (!result.Success)
            {
                await context.Output.WriteAsync(
                    new MovementMessage(MovementDirectionKind.Blocked, _direction, "You"))
                    .ConfigureAwait(false);
                return;
            }

            await _eventBus.PublishAsync(new PlayerMovedEvent(
                context.InvokerEntityId,
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
            _               => Array.Empty<string>(),
        };
    }
}
