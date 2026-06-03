using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Abilities.Events;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Abilities.Commands
{
    public sealed class TeachCommand : ICommand
    {
        private readonly IAbilitySystem _abilitySystem;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly ISessionManager _sessionManager;
        private readonly IPersistenceSystem _persistenceSystem;

        public string Name => "teach";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Grant an ability to a target entity.";
        public string LongDescription =>
            "Teaches a named ability from the ability registry to the target player or entity. " +
            "Triggers an admin boundary save for the student entity.";
        public string Usage => "teach <target> <abilityId>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("target", typeof(string), CommandArgumentKind.Token,
                Required: true, "Player name or entity id."),
            new CommandArgument("abilityId", typeof(string), CommandArgumentKind.Token,
                Required: true, "Ability id from the registry."),
        });

        public TeachCommand(
            IAbilitySystem abilitySystem,
            EntityService entityService,
            IEventBus eventBus,
            ISessionManager sessionManager,
            IPersistenceSystem persistenceSystem)
        {
            _abilitySystem = abilitySystem;
            _entityService = entityService;
            _eventBus = eventBus;
            _sessionManager = sessionManager;
            _persistenceSystem = persistenceSystem;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var targetArg = context.Args.Get<string>("target");
            var abilityId = context.Args.Get<string>("abilityId");

            var studentEntityId = ResolveTarget(targetArg);
            if (studentEntityId == 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"No connected player or entity found for target '{targetArg}'.",
                    OutputSeverity.Error)).ConfigureAwait(false);
                return;
            }

            var taught = _abilitySystem.Teach(context.InvokerEntityId, studentEntityId, abilityId);
            if (!taught)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Could not teach '{abilityId}' (unknown ability or already known).",
                    OutputSeverity.Error)).ConfigureAwait(false);
                return;
            }

            // INV-22 case b: admin boundary save after mutation
            await _persistenceSystem.SaveEntityAsync(studentEntityId).ConfigureAwait(false);

            await _eventBus.PublishAsync(new AbilityLearnedEvent(studentEntityId, abilityId))
                .ConfigureAwait(false);

            await _eventBus.PublishAsync(new AbilityTaughtByAdminEvent(context.InvokerEntityId, studentEntityId, abilityId))
                .ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Taught '{abilityId}' to entity #{studentEntityId}.",
                OutputSeverity.Confirmation)).ConfigureAwait(false);
        }

        private uint ResolveTarget(string target)
        {
            foreach (var session in _sessionManager.GetAll())
            {
                if (session.PlayerEntityId == 0)
                    continue;
                if (_entityService.TryGet<CharacterComponent>(session.PlayerEntityId, out var ch) &&
                    string.Equals(ch.CharacterName, target, StringComparison.OrdinalIgnoreCase))
                    return session.PlayerEntityId;
            }

            if (uint.TryParse(target, out var entityId))
                return entityId;

            return 0;
        }
    }
}
