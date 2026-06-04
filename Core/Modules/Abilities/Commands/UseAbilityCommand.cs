using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Abilities.Events;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Effects.Events;
using Hedron.Core.Output;
using Hedron.Core.Sessions;

namespace Hedron.Core.Modules.Abilities.Commands
{
    public sealed class UseAbilityCommand : ICommand
    {
        private readonly IAbilitySystem _abilitySystem;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly ISessionManager _sessionManager;
        private readonly ICombatSystem _combatSystem;

        public string Name => "useability";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Activate an ability as the invoking entity.";
        public string LongDescription =>
            "Activates a named ability for the invoking admin entity, optionally targeting another player or entity. " +
            "Runtime state changes (pool spend, cooldown, effects) ride the periodic flush — no boundary save.";
        public string Usage => "useability <abilityId> [target]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("abilityId", typeof(string), CommandArgumentKind.Token,
                Required: true, "Ability id from the registry."),
            new CommandArgument("target", typeof(string), CommandArgumentKind.Token,
                Required: false, "Optional player name or entity id to target."),
        });

        public UseAbilityCommand(
            IAbilitySystem abilitySystem,
            EntityService entityService,
            IEventBus eventBus,
            ISessionManager sessionManager,
            ICombatSystem combatSystem)
        {
            _abilitySystem = abilitySystem;
            _entityService = entityService;
            _eventBus = eventBus;
            _sessionManager = sessionManager;
            _combatSystem = combatSystem;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var abilityId = context.Args.Get<string>("abilityId");
            context.Args.TryGet<string>("target", out var targetArg);

            var actor = context.InvokerEntityId;

            uint? targetEntityId = null;
            if (targetArg != null)
            {
                var resolved = ResolveTarget(targetArg, actor);
                if (resolved == 0)
                {
                    await context.Output.WriteAsync(new PlainMessage(
                        $"No connected player or entity found for target '{targetArg}'.",
                        OutputSeverity.Error)).ConfigureAwait(false);
                    return;
                }
                targetEntityId = resolved;
            }

            var result = _abilitySystem.Activate(actor, abilityId, targetEntityId);

            switch (result.Outcome)
            {
                case AbilityActivationOutcome.UnknownAbility:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Unknown ability '{abilityId}'.",
                        OutputSeverity.Error)).ConfigureAwait(false);
                    return;

                case AbilityActivationOutcome.NotKnown:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"You do not know '{abilityId}'.",
                        OutputSeverity.Error)).ConfigureAwait(false);
                    return;

                case AbilityActivationOutcome.NotActivatable:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"'{abilityId}' cannot be activated directly.",
                        OutputSeverity.Error)).ConfigureAwait(false);
                    return;

                case AbilityActivationOutcome.StateBlocked:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Cannot activate — {result.FailReason}.",
                        OutputSeverity.Error)).ConfigureAwait(false);
                    return;

                case AbilityActivationOutcome.OnCooldown:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"'{abilityId}' is on cooldown ({result.FailReason} remaining).",
                        OutputSeverity.Error)).ConfigureAwait(false);
                    return;

                case AbilityActivationOutcome.InsufficientResources:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Not enough {result.FailReason} to activate '{abilityId}'.",
                        OutputSeverity.Error)).ConfigureAwait(false);
                    return;

                case AbilityActivationOutcome.Activated:
                    await HandleActivatedAsync(context, actor, abilityId, targetEntityId, result)
                        .ConfigureAwait(false);
                    return;
            }
        }

        private async Task HandleActivatedAsync(
            CommandContext context,
            uint actor,
            string abilityId,
            uint? targetEntityId,
            AbilityActivationResult result)
        {
            // Publish ability activated event
            await _eventBus.PublishAsync(new AbilityActivatedEvent(actor, abilityId, targetEntityId))
                .ConfigureAwait(false);

            // Publish EffectAppliedEvent for each applied effect
            var resolvedTarget = targetEntityId ?? actor;
            foreach (var effect in result.AppliedEffects)
            {
                await _eventBus.PublishAsync(new EffectAppliedEvent(
                    resolvedTarget, effect.EffectId, effect.Category, effect.Power))
                    .ConfigureAwait(false);
            }

            // Build cost summary
            var costSummary = BuildCostSummary(result.Spent);

            // Build effect count summary
            var effectCount = result.AppliedEffects.Count;
            var effectSummary = effectCount > 0
                ? $" [{effectCount} effect(s) applied]"
                : string.Empty;

            await context.Output.WriteAsync(new PlainMessage(
                $"You invoke {abilityId} (cost: {costSummary}).{effectSummary}",
                OutputSeverity.Confirmation)).ConfigureAwait(false);
        }

        private static string BuildCostSummary(IReadOnlyList<ResourceCost> spent)
        {
            if (spent == null || spent.Count == 0)
                return "none";

            var sb = new StringBuilder();
            for (int i = 0; i < spent.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"{spent[i].Amount} {spent[i].Resource.ToString().ToLowerInvariant()}");
            }
            return sb.ToString();
        }

        private uint ResolveTarget(string target, uint invokerEntityId)
        {
            // 1. Connected player by character name.
            foreach (var session in _sessionManager.GetAll())
            {
                if (session.PlayerEntityId == 0)
                    continue;
                if (_entityService.TryGet<CharacterComponent>(session.PlayerEntityId, out var ch) &&
                    string.Equals(ch.CharacterName, target, StringComparison.OrdinalIgnoreCase))
                    return session.PlayerEntityId;
            }

            // 2. Mob keyword in the invoker's current room (prefix-matched).
            if (_entityService.TryGet<LocationComponent>(invokerEntityId, out var loc) &&
                _combatSystem.TryFindTargetInRoom(loc.RoomEntityId, target, out var mobEntityId))
                return mobEntityId;

            // 3. Numeric entity id fallback.
            if (uint.TryParse(target, out var entityId))
                return entityId;

            return 0;
        }
    }
}
