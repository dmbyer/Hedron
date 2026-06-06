using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.EntityState.Events;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Regeneration.Commands
{
    public sealed class StandCommand : ICommand
    {
        private readonly IEntityStateService _entityStateService;
        private readonly IEventBus _eventBus;

        public string Name => "stand";
        public IReadOnlyList<string> Aliases { get; } = new[] { "wake" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Stand up.";
        public string LongDescription => "Stand up, ending your resting state if active.";
        public string Usage => "stand";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema => CommandArgumentSchema.Empty;

        public StandCommand(IEntityStateService entityStateService, IEventBus eventBus)
        {
            _entityStateService = entityStateService;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (!_entityStateService.IsInState(context.InvokerEntityId, EntityStateFlags.Resting))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You are already standing.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            var oldStates = _entityStateService.GetStates(context.InvokerEntityId);
            _entityStateService.ExitState(context.InvokerEntityId, EntityStateFlags.Resting);
            var newStates = _entityStateService.GetStates(context.InvokerEntityId);

            await context.Output.WriteAsync(
                new PlainMessage("You stand up.", OutputSeverity.System, OutputCategory.System))
                .ConfigureAwait(false);

            await _eventBus.PublishAsync(
                new EntityStateChangedEvent(context.InvokerEntityId, oldStates, newStates))
                .ConfigureAwait(false);
        }
    }
}
