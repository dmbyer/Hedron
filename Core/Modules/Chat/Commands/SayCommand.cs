using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Events;
using Hedron.Core.Modules.Chat.Events;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Chat.Commands
{
    public class SayCommand : ICommand
    {
        private readonly IEventBus _eventBus;

        public string Name => "say";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public string ShortDescription => "Say something to everyone in the room.";
        public string LongDescription => "Broadcasts a message to all players in your current room.";
        public string Usage => "say <message>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("message", typeof(string), CommandArgumentKind.RestOfLine,
                Required: true, "The message to say."),
        });

        public SayCommand(IEventBus eventBus) => _eventBus = eventBus;

        public async Task ExecuteAsync(CommandContext context)
        {
            var message = context.Args.Get<string>("message");
            await _eventBus.PublishAsync(new PlayerSaidEvent(context.InvokerEntityId, message))
                .ConfigureAwait(false);
        }
    }
}
