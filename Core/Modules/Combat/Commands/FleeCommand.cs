using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Combat.Commands
{
    /// <summary>
    /// Player verb <c>flee</c>. Always succeeds — exits combat immediately.
    /// </summary>
    public sealed class FleeCommand : ICommand
    {
        private readonly ICombatSystem _combatSystem;
        private readonly IEntityStateService _entityStateService;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "flee";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Flee from combat.";
        public string LongDescription => "Exit combat immediately. Always succeeds.";
        public string Usage => "flee";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema => CommandArgumentSchema.Empty;

        public FleeCommand(
            ICombatSystem combatSystem,
            IEntityStateService entityStateService,
            EntityService entityService,
            IEventBus eventBus)
        {
            _combatSystem = combatSystem;
            _entityStateService = entityStateService;
            _entityService = entityService;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (!_entityStateService.IsInState(context.InvokerEntityId, EntityStateFlags.InCombat))
            {
                await context.Output.WriteAsync(new PlainMessage("You are not in combat.", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!_entityService.TryGet<CombatStateComponent>(context.InvokerEntityId, out var combatState))
                return;

            var mobEntityId = combatState.OpponentEntityId;

            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
                return;

            _combatSystem.EndCombat(context.InvokerEntityId, mobEntityId);
            _entityStateService.ExitState(context.InvokerEntityId, EntityStateFlags.InCombat);
            _entityStateService.ExitState(mobEntityId, EntityStateFlags.InCombat);

            await _eventBus.PublishAsync(new CombatEndedEvent(
                context.InvokerEntityId, mobEntityId,
                CombatEndOutcome.PlayerFled, location.RoomEntityId))
                .ConfigureAwait(false);
        }
    }
}
