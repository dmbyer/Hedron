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
    public sealed class RestCommand : ICommand
    {
        private readonly IEntityStateService _entityStateService;
        private readonly IEventBus _eventBus;

        public string Name => "rest";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Sit down and rest to recover resources faster.";
        public string LongDescription => "Sit down and enter a resting state, accelerating regeneration of all resource pools.";
        public string Usage => "rest";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema => CommandArgumentSchema.Empty;

        public RestCommand(IEntityStateService entityStateService, IEventBus eventBus)
        {
            _entityStateService = entityStateService;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (_entityStateService.IsInState(context.InvokerEntityId, EntityStateFlags.Resting))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You are already resting.", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            var oldStates = _entityStateService.GetStates(context.InvokerEntityId);

            if (!_entityStateService.TryEnterState(
                    context.InvokerEntityId, EntityStateFlags.Resting, out var failReason))
            {
                await context.Output.WriteAsync(
                    new PlainMessage(failReason!, OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            var newStates = _entityStateService.GetStates(context.InvokerEntityId);

            await context.Output.WriteAsync(
                new PlainMessage("You sit down and begin to rest.", OutputSeverity.System))
                .ConfigureAwait(false);

            await _eventBus.PublishAsync(
                new EntityStateChangedEvent(context.InvokerEntityId, oldStates, newStates))
                .ConfigureAwait(false);
        }
    }
}
