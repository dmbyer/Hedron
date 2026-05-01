using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Events;
using Hedron.Core.Modules.Chat.Events;
using Hedron.Core.Sessions;

namespace Hedron.Core.Modules.Chat.Commands
{
    public class SayCommand : ICommand
    {
        private readonly IEventBus _eventBus;

        public string Name => "say";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();

        public SayCommand(IEventBus eventBus) => _eventBus = eventBus;

        public async Task ExecuteAsync(ISession session, string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                await session.SendLineAsync("Say what?").ConfigureAwait(false);
                return;
            }

            await _eventBus.PublishAsync(new PlayerSaidEvent(session.PlayerEntityId, arguments))
                .ConfigureAwait(false);
        }
    }
}
