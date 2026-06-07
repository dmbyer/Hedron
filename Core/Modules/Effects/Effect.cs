using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Effects
{
    public enum EffectKind
    {
        StatModifier,
        Instant,
        Periodic,
        GrantFlag,
        GrantAbility,
        Trigger,
        TransformModifier,
    }

    public enum EffectCategory
    {
        Buff,
        Debuff,
        Curse,
        Disease,
        Blessing,
        Poison,
        Aura,
    }

    public enum EffectLifetime
    {
        Instant,
        Timed,
        UntilRemoved,
        WhileEquipped,
        WhileKnown,
        WhilePresent,
    }

    public enum StackPolicy
    {
        Stack,
        HighestWins,
        Refresh,
        UniquePerSource,
        Replace,
    }

    // HoT should have Early/Normal, DoT should have Late so heals fire before damage in the same tick.
    public enum EffectPhase
    {
        Early,
        Normal,
        Late,
    }

    public sealed record EffectSource(uint EntityId, string? SourceLabel = null);

    // TODO: migrate Aspect from string? to AspectComposition? (deferred from slice 11-d — aspect-foundation)
    public sealed record EffectParams(ScoreId TargetScore, int BaseMagnitude, string? Aspect = null);

    public sealed record Effect(
        string EffectId,
        EffectKind Kind,
        EffectParams Params,
        EffectCategory Category,
        int Power,
        EffectSource Source,
        string? Group,
        EffectLifetime Lifetime,
        float Duration,
        float Elapsed,
        StackPolicy Stacking,
        EffectPhase Phase
    );

    public sealed record PeriodicApplication(uint EntityId, Effect Effect, int Magnitude);

    public sealed record EffectTickResult(
        IReadOnlyList<PeriodicApplication> DueApplications,
        IReadOnlyList<(uint EntityId, Effect Effect)> Expired
    );

    public sealed record EffectDefinition(
        string EffectId,
        EffectKind Kind,
        EffectParams Params,
        EffectCategory Category,
        string PowerScalingFormula,
        float Duration,
        StackPolicy Stacking,
        EffectPhase Phase
    );
}
