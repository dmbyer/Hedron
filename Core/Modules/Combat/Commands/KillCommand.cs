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
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Combat.Commands
{
    /// <summary>
    /// Player verb <c>kill &lt;mob&gt;</c> (alias <c>k</c>). Initiates melee combat.
    /// </summary>
    public sealed class KillCommand : ICommand
    {
        private readonly ICombatSystem _combatSystem;
        private readonly IEntityStateService _entityStateService;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly ILogger<KillCommand> _logger;

        public string Name => "kill";
        public IReadOnlyList<string> Aliases { get; } = new[] { "k" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Attack a target.";
        public string LongDescription => "Initiates melee combat with a mob in your current room.";
        public string Usage => "kill <target>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new CommandArgumentSchema(new[]
        {
            new CommandArgument("target", typeof(string), CommandArgumentKind.RestOfLine,
                Required: true, "Name or keyword of the mob to attack.", null),
        });

        public KillCommand(
            ICombatSystem combatSystem,
            IEntityStateService entityStateService,
            EntityService entityService,
            IEventBus eventBus,
            ILogger<KillCommand> logger)
        {
            _combatSystem = combatSystem;
            _entityStateService = entityStateService;
            _entityService = entityService;
            _eventBus = eventBus;
            _logger = logger;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (!context.Args.TryGet<string>("target", out var target) || string.IsNullOrWhiteSpace(target))
            {
                await context.Output.WriteAsync(new PlainMessage("Kill what?", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            if (_entityStateService.IsInState(context.InvokerEntityId, EntityStateFlags.InCombat))
            {
                await context.Output.WriteAsync(new PlainMessage("You are already fighting!", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
                return;

            if (!_combatSystem.TryFindTargetInRoom(location.RoomEntityId, target, out var mobEntityId))
            {
                await context.Output.WriteAsync(new PlainMessage("You don't see that here.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!_entityStateService.TryEnterState(context.InvokerEntityId, EntityStateFlags.InCombat, out var failReason))
            {
                await context.Output.WriteAsync(new PlainMessage(failReason!, OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!_entityStateService.TryEnterState(mobEntityId, EntityStateFlags.InCombat, out _))
            {
                _logger.LogWarning(
                    "KillCommand: mob {MobEntityId} rejected InCombat state; proceeding anyway.",
                    mobEntityId);
            }

            _combatSystem.StartCombat(context.InvokerEntityId, mobEntityId);

            await _eventBus.PublishAsync(new CombatStartedEvent(
                context.InvokerEntityId, mobEntityId, location.RoomEntityId))
                .ConfigureAwait(false);
        }
    }
}
