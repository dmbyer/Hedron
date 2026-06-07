using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Abilities.Events;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Effects.Events;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Output;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Abilities.Commands
{
    /// <summary>
    /// Shared invocation pipeline used by both <see cref="SkillInvocationCommand"/> and
    /// <see cref="CastCommand"/>. Encapsulates: state-aware target resolution, combat entry,
    /// <see cref="IAbilitySystem.Activate"/> call, event publication, and ability strike.
    /// <para>
    /// Neither command nor system — this is an internal orchestration helper owned by the
    /// Abilities module. It publishes events (INV-8: initiators may publish) but contains no
    /// domain logic; all domain decisions are delegated to systems.
    /// </para>
    /// </summary>
    public sealed class AbilityInvocationPipeline
    {
        private readonly IAbilitySystem _abilitySystem;
        private readonly ICombatSystem _combatSystem;
        private readonly IEntityStateService _entityStateService;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly ILogger<AbilityInvocationPipeline> _logger;

        public AbilityInvocationPipeline(
            IAbilitySystem abilitySystem,
            ICombatSystem combatSystem,
            IEntityStateService entityStateService,
            EntityService entityService,
            IEventBus eventBus,
            ILogger<AbilityInvocationPipeline> logger)
        {
            _abilitySystem = abilitySystem ?? throw new ArgumentNullException(nameof(abilitySystem));
            _combatSystem = combatSystem ?? throw new ArgumentNullException(nameof(combatSystem));
            _entityStateService = entityStateService ?? throw new ArgumentNullException(nameof(entityStateService));
            _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes the full ability invocation pipeline.
        /// </summary>
        /// <param name="actorId">Entity invoking the ability.</param>
        /// <param name="abilityId">Ability identifier (already validated as known to the actor).</param>
        /// <param name="def">Resolved ability definition.</param>
        /// <param name="rawTargetToken">Raw target token from input (may be null or whitespace).</param>
        /// <param name="output">Writer for user-facing messages.</param>
        /// <param name="loggerContext">Caller type name for warning messages.</param>
        public async Task InvokeAsync(
            uint actorId,
            string abilityId,
            AbilityDefinition def,
            string? rawTargetToken,
            IOutputWriter output,
            string loggerContext)
        {
            bool isOffensive = _abilitySystem.IsOffensive(abilityId);
            bool wasInCombat = _entityStateService.IsInState(actorId, EntityStateFlags.InCombat);

            // 1. State-aware target resolution.
            uint? resolvedTarget = await ResolveTargetAsync(actorId, def, rawTargetToken, isOffensive, output)
                .ConfigureAwait(false);

            if (resolvedTarget == null && def.Targeting == Targeting.Target && isOffensive)
                return; // friendly error already written inside ResolveTargetAsync

            // 2. Offensive opens combat if not already fighting.
            if (isOffensive && resolvedTarget.HasValue
                && !_entityStateService.IsInState(actorId, EntityStateFlags.InCombat))
            {
                if (!_entityStateService.TryEnterState(actorId, EntityStateFlags.InCombat, out var failReason))
                {
                    await output.WriteAsync(new PlainMessage(failReason!, OutputSeverity.System, OutputCategory.System))
                        .ConfigureAwait(false);
                    return;
                }

                if (!_entityStateService.TryEnterState(resolvedTarget.Value, EntityStateFlags.InCombat, out _))
                    _logger.LogWarning(
                        "{Context}: target {Id} rejected InCombat state; proceeding anyway.",
                        loggerContext, resolvedTarget.Value);

                _combatSystem.StartCombat(actorId, resolvedTarget.Value);

                if (!_entityService.TryGet<LocationComponent>(actorId, out var loc1)) return;
                await _eventBus.PublishAsync(new CombatStartedEvent(actorId, resolvedTarget.Value, loc1.RoomEntityId))
                    .ConfigureAwait(false);
            }

            // 3. Activate (with offensive opt-out so AbilitySystem skips raw HP deduction).
            var result = _abilitySystem.Activate(actorId, abilityId, resolvedTarget, resolveOffensiveExternally: isOffensive);
            if (result.Outcome != AbilityActivationOutcome.Activated)
            {
                await WriteFailureAsync(output, result, def.Name).ConfigureAwait(false);
                return;
            }

            // 4. Publish AbilityActivatedEvent + per-effect EffectAppliedEvents.
            await _eventBus.PublishAsync(new AbilityActivatedEvent(actorId, abilityId, resolvedTarget))
                .ConfigureAwait(false);

            foreach (var effect in result.AppliedEffects)
            {
                await _eventBus.PublishAsync(new EffectAppliedEvent(
                    resolvedTarget ?? actorId, effect.EffectId, effect.Category, effect.Power))
                    .ConfigureAwait(false);
            }

            // 5. Offensive strike — always-hits, defense-mitigated, aspect-resolved.
            if (isOffensive && resolvedTarget.HasValue && result.OffensivePower.HasValue)
            {
                if (!_entityService.TryGet<LocationComponent>(actorId, out var loc2)) return;
                var defenderName = GetEntityName(resolvedTarget.Value);
                // Composition source: the ability's migrated Aspect field (INV-6 point-in-time capture).
                var composition = def.Aspect;
                var strikeResult = _combatSystem.ResolveAbilityStrike(
                    actorId, resolvedTarget.Value, result.OffensivePower.Value, composition);
                await _eventBus.PublishAsync(new AbilityStrikeResolvedEvent(
                    actorId, resolvedTarget.Value, loc2.RoomEntityId, strikeResult, abilityId, defenderName,
                    AspectComposition: strikeResult.AspectComposition))
                    .ConfigureAwait(false);
            }

            // If the player was already in combat when this ability fired, defer the
            // command-end flush so this output batches into the next tick-end flush
            // together with the regular combat round. First-use (opening combat) never
            // sets wasInCombat, so it always flushes immediately for the entry narrative.
            if (wasInCombat)
                output.DeferFlush();
        }

        // -----------------------------------------------------------------------

        private async Task<uint?> ResolveTargetAsync(
            uint actorId,
            AbilityDefinition def,
            string? rawToken,
            bool isOffensive,
            IOutputWriter output)
        {
            if (def.Targeting == Targeting.Self)
                return actorId; // ignore any token

            // Targeting.Target:
            string? token = string.IsNullOrWhiteSpace(rawToken) ? null : rawToken!.Trim();

            if (token != null)
            {
                // Explicit target: prefix-match mob keywords in the room.
                if (!_entityService.TryGet<LocationComponent>(actorId, out var loc)) return null;
                if (!_combatSystem.TryFindTargetInRoom(loc.RoomEntityId, token, out var mobId))
                {
                    await output.WriteAsync(new PlainMessage("You don't see that here.", OutputSeverity.System, OutputCategory.System))
                        .ConfigureAwait(false);
                    return null;
                }
                return mobId;
            }

            // No explicit token — use current combat opponent if in combat.
            if (_entityStateService.IsInState(actorId, EntityStateFlags.InCombat))
            {
                if (_entityService.TryGet<CombatStateComponent>(actorId, out var cs))
                    return cs.OpponentEntityId;
            }

            if (isOffensive)
            {
                // Not in combat, no token, offensive → friendly prompt.
                await output.WriteAsync(new PlainMessage($"{def.Name} whom?", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return null;
            }

            return null; // non-offensive with no token and no combat — no target needed
        }

        private async Task WriteFailureAsync(IOutputWriter output, AbilityActivationResult result, string abilityName)
        {
            string message = result.Outcome switch
            {
                AbilityActivationOutcome.UnknownAbility =>
                    $"Unknown ability '{result.AbilityId}'.",
                AbilityActivationOutcome.NotKnown =>
                    $"You do not know '{result.AbilityId}'.",
                AbilityActivationOutcome.NotActivatable =>
                    $"'{result.AbilityId}' cannot be activated directly.",
                AbilityActivationOutcome.StateBlocked =>
                    $"Cannot activate — {result.FailReason}.",
                AbilityActivationOutcome.OnCooldown =>
                    $"'{abilityName}' is on cooldown ({result.FailReason} remaining).",
                AbilityActivationOutcome.InsufficientResources =>
                    $"Not enough {result.FailReason} to use {abilityName}.",
                _ =>
                    $"Cannot use {abilityName} right now.",
            };

            await output.WriteAsync(new PlainMessage(message, OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
        }

        private string GetEntityName(uint entityId)
        {
            if (_entityService.TryGet<PlayerComponent>(entityId, out var p)) return p.DisplayName;
            if (_entityService.TryGet<MobDataComponent>(entityId, out var m)) return m.Name;
            return "someone";
        }
    }
}
