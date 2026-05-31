using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Effects.Events;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Output;
using Hedron.Core.Sessions;

namespace Hedron.Core.Modules.Effects.Commands
{
    public sealed class AffectCommand : ICommand
    {
        private readonly IEffectSystem _effectSystem;
        private readonly IEffectRegistry _effectRegistry;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly ISessionManager _sessionManager;

        public string Name => "affect";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Apply a registry effect to a target entity.";
        public string LongDescription =>
            "Applies a named effect from the effect registry to the target player or entity. " +
            "Optionally overrides the effect power for testing.";
        public string Usage => "affect <target> <effectId> [power]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("target", typeof(string), CommandArgumentKind.Token,
                Required: true, "Player name or entity id."),
            new CommandArgument("effectId", typeof(string), CommandArgumentKind.Token,
                Required: true, "Effect id from the registry (e.g. empower, poison)."),
            new CommandArgument("power", typeof(string), CommandArgumentKind.Token,
                Required: false, "Optional integer power override."),
        });

        public AffectCommand(
            IEffectSystem effectSystem,
            IEffectRegistry effectRegistry,
            EntityService entityService,
            IEventBus eventBus,
            ISessionManager sessionManager)
        {
            _effectSystem = effectSystem;
            _effectRegistry = effectRegistry;
            _entityService = entityService;
            _eventBus = eventBus;
            _sessionManager = sessionManager;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var targetArg = context.Args.Get<string>("target");
            var effectId = context.Args.Get<string>("effectId");
            var powerArg = context.Args.Get<string?>("power");

            if (!_effectRegistry.TryGet(effectId, out var definition))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Unknown effect '{effectId}'. Use 'effects list' to see available effects.",
                    OutputSeverity.Error)).ConfigureAwait(false);
                return;
            }

            var targetEntityId = ResolveTarget(targetArg);
            if (targetEntityId == 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"No connected player or entity found for target '{targetArg}'.",
                    OutputSeverity.Error)).ConfigureAwait(false);
                return;
            }

            int? overridePower = null;
            if (powerArg != null && int.TryParse(powerArg, out var parsedPower))
                overridePower = parsedPower;

            var appliedDef = overridePower.HasValue
                ? definition with { Params = definition.Params with { BaseMagnitude = overridePower.Value }, PowerScalingFormula = "fixed" }
                : definition;

            var appliedEffect = _effectSystem.Apply(targetEntityId, appliedDef, context.InvokerEntityId);
            if (appliedEffect == null)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Effect '{effectId}' was not applied (HighestWins policy: existing effect has equal or greater power).",
                    OutputSeverity.Error)).ConfigureAwait(false);
                return;
            }

            await _eventBus.PublishAsync(new EffectAppliedEvent(
                targetEntityId, effectId, definition.Category, appliedEffect.Power))
                .ConfigureAwait(false);

            await _eventBus.PublishAsync(new EffectAppliedByAdminEvent(
                context.InvokerEntityId, targetEntityId, effectId, appliedEffect.Power))
                .ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Applied '{effectId}' (power {appliedEffect.Power}) to entity #{targetEntityId}.",
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
