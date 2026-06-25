using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Effects.Events;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Output;
using Hedron.Core.Sessions;

namespace Hedron.Core.Modules.Effects.Commands
{
    public sealed class AffectCommand : ICommand
    {
        private readonly IEffectSystem _effectSystem;
        private readonly IEffectRegistry _effectRegistry;
        private readonly IAttributeSystem _attributeSystem;
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
            IAttributeSystem attributeSystem,
            EntityService entityService,
            IEventBus eventBus,
            ISessionManager sessionManager)
        {
            _effectSystem = effectSystem;
            _effectRegistry = effectRegistry;
            _attributeSystem = attributeSystem;
            _entityService = entityService;
            _eventBus = eventBus;
            _sessionManager = sessionManager;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var targetArg = context.Args.Get<string>("target");
            var effectId = context.Args.Get<string>("effectId");
            context.Args.TryGet<string>("power", out var powerArg);

            if (!_effectRegistry.TryGet(effectId, out var definition))
            {
                var available = string.Join(", ", _effectRegistry.AllIds);
                await context.Output.WriteAsync(new PlainMessage(
                    $"Unknown effect '{effectId}'. Available: {available}.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            var targetEntityId = ResolveTarget(targetArg);
            if (targetEntityId == 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"No connected player or entity found for target '{targetArg}'.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            int? overridePower = null;
            if (powerArg != null && int.TryParse(powerArg, out var parsedPower))
                overridePower = parsedPower;

            // Preserve the direction (sign) of the definition's BaseMagnitude; treat the override
            // as a magnitude. This means `affect goblin kick_damage 5` deals 5 damage (not 5 healing)
            // because kick_damage's BaseMagnitude is negative. For effects with BaseMagnitude == 0,
            // fall back to the raw override value.
            EffectDefinition appliedDef;
            if (overridePower.HasValue)
            {
                var baseMagnitude = definition.Params.BaseMagnitude;
                var signedOverride = baseMagnitude != 0
                    ? Math.Sign(baseMagnitude) * Math.Abs(overridePower.Value)
                    : overridePower.Value;
                appliedDef = definition with { Params = definition.Params with { BaseMagnitude = signedOverride }, PowerScalingFormula = "fixed" };
            }
            else
            {
                appliedDef = definition;
            }

            var applyResult = _effectSystem.Apply(targetEntityId, appliedDef, context.InvokerEntityId);

            // Gate B — surface immune / stacking-blocked outcomes before publishing events.
            if (applyResult is EffectApplyResult.NotApplied notApplied)
            {
                string notAppliedMsg = notApplied.Reason == EffectNotAppliedReason.Immune
                    ? $"The target is immune and the effect '{effectId}' did not take hold."
                    : $"Effect '{effectId}' was not applied (HighestWins policy: existing effect has equal or greater power).";

                await context.Output.WriteAsync(new PlainMessage(
                    notAppliedMsg,
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            var appliedEffect = ((EffectApplyResult.Applied)applyResult).Effect;

            if (appliedEffect.Kind == EffectKind.Instant)
                ApplyInstantMagnitude(targetEntityId, appliedEffect.Params.TargetScore, appliedEffect.Power);

            await _eventBus.PublishAsync(new EffectAppliedEvent(
                targetEntityId, effectId, definition.Category, appliedEffect.Power))
                .ConfigureAwait(false);

            await _eventBus.PublishAsync(new EffectAppliedByAdminEvent(
                context.InvokerEntityId, targetEntityId, effectId, appliedEffect.Power))
                .ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Applied '{effectId}' (power {appliedEffect.Power}) to entity #{targetEntityId}.",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }

        private void ApplyInstantMagnitude(uint entityId, ScoreId scoreId, int power)
        {
            switch (scoreId)
            {
                case ScoreId.HpCurrent:
                    _attributeSystem.SetCurrentHp(entityId, _attributeSystem.GetCurrentHp(entityId) + power);
                    break;
                case ScoreId.ManaCurrent:
                    _attributeSystem.SetCurrentMana(entityId, _attributeSystem.GetCurrentMana(entityId) + power);
                    break;
                case ScoreId.StaminaCurrent:
                    _attributeSystem.SetCurrentStamina(entityId, _attributeSystem.GetCurrentStamina(entityId) + power);
                    break;
                case ScoreId.AstraCurrent:
                    _attributeSystem.SetCurrentAstra(entityId, _attributeSystem.GetCurrentAstra(entityId) + power);
                    break;
            }
        }

        private uint ResolveTarget(string target)
        {
            // Connected players — match by character name
            foreach (var session in _sessionManager.GetAll())
            {
                if (session.PlayerEntityId == 0)
                    continue;
                if (_entityService.TryGet<CharacterComponent>(session.PlayerEntityId, out var ch) &&
                    string.Equals(ch.CharacterName, target, StringComparison.OrdinalIgnoreCase))
                    return session.PlayerEntityId;
            }

            // Mobs — match by name or any keyword (first match wins)
            foreach (var (entityId, mob) in _entityService.GetAllComponents<MobDataComponent>())
            {
                if (string.Equals(mob.Name, target, StringComparison.OrdinalIgnoreCase))
                    return entityId;
                if (mob.Keywords.Any(k => string.Equals(k, target, StringComparison.OrdinalIgnoreCase)))
                    return entityId;
            }

            // Numeric entity id fallback
            if (uint.TryParse(target, out var parsed))
                return parsed;

            return 0;
        }
    }
}
