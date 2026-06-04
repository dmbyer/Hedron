using System;
using System.Collections.Generic;
using Hedron.Core.Modules.Effects;

namespace Hedron.Core.Modules.Abilities.Systems
{
    public enum AbilityActivationOutcome
    {
        Activated,
        UnknownAbility,
        NotKnown,
        NotActivatable,       // Passive or Triggered ability
        StateBlocked,         // Entity state prevents activation (e.g. Incapacitated)
        OnCooldown,
        InsufficientResources,
    }

    public sealed record AbilityActivationResult(
        AbilityActivationOutcome Outcome,
        string AbilityId,
        IReadOnlyList<Effect> AppliedEffects,
        IReadOnlyList<ResourceCost> Spent,
        float CooldownSeconds,
        string? FailReason = null,
        int? OffensivePower = null
    );

    public interface IAbilitySystem
    {
        /// <summary>
        /// Activates an ability for the actor against the optional target.
        /// When <paramref name="resolveOffensiveExternally"/> is <c>true</c>, any offensive
        /// damage effect (Instant/Periodic, TargetScore == HpCurrent, BaseMagnitude &lt; 0) is
        /// skipped by <see cref="Effects.Systems.IEffectSystem"/> and its raw magnitude is
        /// returned as <see cref="AbilityActivationResult.OffensivePower"/> instead. All other
        /// effects and resource/cooldown handling are unchanged.
        /// </summary>
        AbilityActivationResult Activate(uint actorEntityId, string abilityId, uint? targetEntityId = null, bool resolveOffensiveExternally = false);

        /// <summary>
        /// Returns <c>true</c> if the ability exists, has <see cref="Targeting.Target"/>,
        /// and at least one of its effects is an offensive damage effect
        /// (Instant or Periodic kind, TargetScore == HpCurrent, BaseMagnitude &lt; 0).
        /// </summary>
        bool IsOffensive(string abilityId);

        bool Learn(uint entityId, string abilityId);
        bool Teach(uint teacherEntityId, uint studentEntityId, string abilityId);
        IReadOnlyList<string> GetKnown(uint entityId);
        bool IsKnown(uint entityId, string abilityId);
        float GetCooldownRemaining(uint entityId, string abilityId);
        IReadOnlyList<(string AbilityId, float CooldownRemaining)> GetCooldowns(uint entityId);
        void AdvanceCooldowns(TimeSpan elapsed);
    }
}
