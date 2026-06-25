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

    /// <summary>
    /// Reason a non-immune <see cref="EffectApplyResult"/> was not applied.
    /// Distinguishes the stacking-policy rejection from the protection-immunity rejection
    /// so callers can produce the correct player message.
    /// </summary>
    public enum EffectNotAppliedReason
    {
        /// <summary>Stacking policy (e.g. HighestWins) blocked the application.</summary>
        StackingPolicy,
        /// <summary>Target carries <c>EffectImmune</c> — the effect cannot land.</summary>
        Immune,
    }

    /// <summary>
    /// Structured result returned by <see cref="Systems.IEffectSystem.Apply"/>.
    /// Replaces the previous nullable <see cref="Effect"/> to distinguish "not applied
    /// (stacking)" from "not applied (immune)" so callers phrase messages correctly.
    /// </summary>
    public abstract record EffectApplyResult
    {
        private EffectApplyResult() { }

        /// <summary>Effect was applied (may be instant — not stored in <c>EffectsComponent</c>).</summary>
        public sealed record Applied(Effect Effect) : EffectApplyResult;

        /// <summary>Effect was NOT applied; <see cref="Reason"/> explains why.</summary>
        public sealed record NotApplied(EffectNotAppliedReason Reason) : EffectApplyResult;

        // ── Convenience factories ──────────────────────────────────────────────

        /// <summary>Returns an <see cref="Applied"/> result for the given effect.</summary>
        public static EffectApplyResult ForApplied(Effect effect) => new Applied(effect);

        /// <summary>Returns a <see cref="NotApplied"/> result for an immunity refusal.</summary>
        public static EffectApplyResult Immune { get; } = new NotApplied(EffectNotAppliedReason.Immune);

        /// <summary>Returns a <see cref="NotApplied"/> result for a stacking-policy rejection.</summary>
        public static EffectApplyResult StackingBlocked { get; } = new NotApplied(EffectNotAppliedReason.StackingPolicy);
    }
}
