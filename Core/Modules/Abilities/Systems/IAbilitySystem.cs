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
        string? FailReason = null
    );

    public interface IAbilitySystem
    {
        AbilityActivationResult Activate(uint actorEntityId, string abilityId, uint? targetEntityId = null);
        bool Learn(uint entityId, string abilityId);
        bool Teach(uint teacherEntityId, uint studentEntityId, string abilityId);
        IReadOnlyList<string> GetKnown(uint entityId);
        bool IsKnown(uint entityId, string abilityId);
        float GetCooldownRemaining(uint entityId, string abilityId);
        IReadOnlyList<(string AbilityId, float CooldownRemaining)> GetCooldowns(uint entityId);
        void AdvanceCooldowns(TimeSpan elapsed);
    }
}
