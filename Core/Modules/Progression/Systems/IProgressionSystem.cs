using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Progression.Systems
{
    /// <summary>Per-track outcome of a single <see cref="IProgressionSystem.AwardExperience"/> call.</summary>
    public readonly record struct AwardOutcome(
        ProgressionTrack Track,
        int AmountAwarded,
        int ImprovementsGained,
        int NewImprovementCount);

    /// <summary>
    /// What the trigger knows about the action being rewarded — the mechanical translation of an
    /// event's fields, produced by <c>AdvancementHandler</c>. The rule decides what to do with it;
    /// the handler decides nothing (INV-8).
    /// </summary>
    /// <param name="SubjectAbilityId">
    /// The ability the action used, when the trigger names one. Combined with
    /// <see cref="AdvancementRule.IncludesSubjectTrack"/> it adds that ability's own track.
    /// </param>
    /// <param name="SubjectAttributeTrack">
    /// The score track the subject content routes its attribute XP to. When null, the rule's
    /// <see cref="AdvancementRule.StaticTracks"/> are used instead.
    /// </param>
    /// <param name="ContentScale">
    /// The per-content granular scale (R7) — an ability's <c>XpScale</c> or a mob's <c>XpScale</c>.
    /// <c>0</c> awards nothing from that piece of content.
    /// </param>
    /// <param name="Magnitude">
    /// The action's size, when the rule gates on it — damage dealt, for the damage-taken rule.
    /// </param>
    /// <param name="OpponentEntityId">
    /// The other party, when the rule needs it for the anti-grind power ratio (the victim, for a kill).
    /// </param>
    public readonly record struct UseAwardContext(
        string? SubjectAbilityId = null,
        ScoreId? SubjectAttributeTrack = null,
        double ContentScale = 1.0,
        int Magnitude = 0,
        uint OpponentEntityId = 0);

    /// <summary>
    /// Per-candidate-track outcome of one advancement-rule evaluation. A row with
    /// <see cref="AwardOutcome.AmountAwarded"/> <c>== 0</c> means the candidate was rolled and
    /// missed (or was ineligible) — the caller publishes nothing for it.
    /// </summary>
    public sealed record UseAwardResult(IReadOnlyList<AwardOutcome> Tracks);

    /// <summary>Per-track outcome of a combat kill award — the shape the balance sandbox reduces over.</summary>
    public sealed record CombatAwardResult(IReadOnlyList<AwardOutcome> Tracks);

    /// <summary>
    /// Use-driven experience accrual and threshold-improvement for the progression substrate
    /// (gameplay-model Spine E). A track is keyed by <see cref="ProgressionTrack"/> — one engine,
    /// one component, one threshold curve across score and ability tracks. Reads raw
    /// <c>AttributesComponent</c> fields directly (not <c>IStatSystem</c>) for the anti-grind
    /// power proxy — going through <c>IStatSystem</c> would create a DI cycle back through the
    /// <c>IEffectContributor</c> port this system's own contributor registers on. Never touches
    /// the event bus (INV-5) — callers publish the result.
    /// </summary>
    public interface IProgressionSystem
    {
        /// <summary>
        /// <b>The single entry point every XP source flows through.</b> Looks up
        /// <paramref name="source"/>'s <see cref="AdvancementRule"/>, evaluates its
        /// <see cref="AdvancementEligibility"/> against <paramref name="context"/>, builds the
        /// candidate track list, and for each candidate rolls the rank-decayed chance and — on
        /// success — draws and scales the base amount before awarding it.
        ///
        /// <para>
        /// <b>Draw contract (INV-26).</b> A rule whose effective chance is <c>&gt;= 1.0</c>
        /// short-circuits with <b>no</b> <c>IRandom</c> call, and an ineligible candidate consumes
        /// <b>zero</b> draws. The combat-kill row is exactly the first case and the trivial-victim
        /// path exactly the second, so kills draw what they have always drawn and the shared
        /// seeded stream in the balance sandbox does not shift.
        /// </para>
        ///
        /// <para>Publishes nothing (INV-5) — the caller publishes per row.</para>
        /// </summary>
        UseAwardResult AwardUseExperience(uint entityId, XpSource source, UseAwardContext context);

        /// <summary>
        /// Adds <paramref name="amount"/> to <paramref name="track"/>'s cumulative XP for
        /// <paramref name="entityId"/>, creating the component/track entry on first award, then
        /// resolves any threshold crossings. A non-positive <paramref name="amount"/> is a no-op —
        /// no entry is created and <see cref="AwardOutcome.ImprovementsGained"/> is 0.
        /// </summary>
        AwardOutcome AwardExperience(uint entityId, ProgressionTrack track, int amount, XpSource source);

        /// <inheritdoc cref="AwardExperience(uint, ProgressionTrack, int, XpSource)"/>
        AwardOutcome AwardExperience(uint entityId, ScoreId track, int amount, XpSource source);

        /// <summary>
        /// Increments <paramref name="track"/>'s improvement count once per threshold the
        /// entity's current cumulative XP has crossed (a single large award can cross several).
        /// Returns the number of improvements gained by this call. Safe to call on an entity with
        /// no <see cref="Components.ProgressionComponent"/> — returns 0, creates nothing.
        /// </summary>
        int TryImprove(uint entityId, ProgressionTrack track);

        /// <inheritdoc cref="TryImprove(uint, ProgressionTrack)"/>
        int TryImprove(uint entityId, ScoreId track);

        /// <summary>
        /// Resolves a combat-kill award through <see cref="AwardUseExperience"/>'s
        /// <see cref="XpSource.CombatKill"/> row, resolving the victim's <c>MobDataComponent.XpScale</c>
        /// <b>internally</b> so a live kill and a sandbox kill cannot drift (the sandbox calls this
        /// directly and would otherwise never see a scale applied by the handler).
        /// Publishes nothing (INV-5).
        /// </summary>
        CombatAwardResult AwardCombatExperience(uint killerEntityId, uint victimEntityId);

        /// <summary>Cumulative XP earned so far for <paramref name="track"/>. 0 if never awarded.</summary>
        int GetXp(uint entityId, ProgressionTrack track);

        /// <inheritdoc cref="GetXp(uint, ProgressionTrack)"/>
        int GetXp(uint entityId, ScoreId track);

        /// <summary>
        /// Number of thresholds crossed for <paramref name="track"/> — the power-step count for a
        /// score track, the displayed rank for an ability track. 0 if never awarded.
        /// </summary>
        int GetImprovementCount(uint entityId, ProgressionTrack track);

        /// <inheritdoc cref="GetImprovementCount(uint, ProgressionTrack)"/>
        int GetImprovementCount(uint entityId, ScoreId track);

        /// <summary>Cumulative XP still needed to cross the next threshold for <paramref name="track"/>.</summary>
        int GetXpToNextThreshold(uint entityId, ProgressionTrack track);

        /// <inheritdoc cref="GetXpToNextThreshold(uint, ProgressionTrack)"/>
        int GetXpToNextThreshold(uint entityId, ScoreId track);

        /// <summary>
        /// Every <b>score</b> track the entity has ever been awarded XP or an improvement on.
        /// Ability tracks are excluded by construction — this is the list
        /// <see cref="ProgressionEffectContributor"/> folds into power, and ability rank grants
        /// none (D3).
        /// </summary>
        IReadOnlyList<ScoreId> GetTrackedScores(uint entityId);

        /// <summary>Every track — score and ability — the entity has ever earned on.</summary>
        IReadOnlyList<ProgressionTrack> GetTrackedTracks(uint entityId);
    }
}
