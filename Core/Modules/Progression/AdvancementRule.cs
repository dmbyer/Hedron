using System.Collections.Generic;

namespace Hedron.Core.Modules.Progression
{
    /// <summary>
    /// One row of the advancement table: everything <c>ProgressionSystem.AwardUseExperience</c>
    /// needs to turn a trigger into an award. Adding an XP source is adding a row here plus the
    /// mechanical event→context mapping in <c>AdvancementHandler</c> — never a new handler and
    /// never a second improvement engine (INV-19: the third repetition promoted the per-source
    /// handler pattern to this registry).
    ///
    /// <para>
    /// Rows are compiled (configuration Category 3 — System Math/Balance) in
    /// <see cref="ProgressionConstants.Rules"/>. Promotion to a data file is deferred to a
    /// demonstrated recompile-free need (OD-2): these numbers are pinned by CI simulation
    /// goldens, so a live-editable form would need the pinning contract reworked first.
    /// </para>
    /// </summary>
    /// <param name="Source">The trigger vocabulary key this row answers to.</param>
    /// <param name="StaticTracks">
    /// The row's <b>fallback</b> attribute tracks: used only when the trigger names no subject
    /// attribute track. A context that supplies <see cref="Systems.UseAwardContext.SubjectAttributeTrack"/>
    /// replaces these rather than adding to them — the subject's own routing wins.
    /// </param>
    /// <param name="IncludesSubjectTrack">
    /// When <see langword="true"/> and the trigger names a subject ability, that ability's own
    /// track is a candidate alongside the attribute track(s).
    /// </param>
    /// <param name="Eligibility">What the rule requires of the attempt — see <see cref="AdvancementEligibility"/>.</param>
    /// <param name="BaseAwardMin">Inclusive lower bound of the randomized per-track base amount.</param>
    /// <param name="BaseAwardMax">Inclusive upper bound of the randomized per-track base amount.</param>
    /// <param name="BaseChance">
    /// Probability the candidate awards at all, before rank decay. <c>&gt;= 1.0</c> paired with a
    /// zero <paramref name="ChanceDecayPerImprovement"/> short-circuits the roll and consumes
    /// <b>no</b> <c>IRandom</c> draw (the combat-kill row relies on this, INV-26).
    /// </param>
    /// <param name="ChanceDecayPerImprovement">
    /// Divisor growth per improvement already earned on the candidate track:
    /// <c>chance = BaseChance / (1 + improvements × decay)</c>. This is the <i>second</i>
    /// rate-slowing curve — it composes with the growing XP threshold, deliberately, rather than
    /// curving the power step (which stays linear).
    /// </param>
    /// <param name="SourceScale">Per-source granular tuning multiplier (R7). Ships at <c>1.0</c>.</param>
    public sealed record AdvancementRule(
        XpSource Source,
        IReadOnlyList<ProgressionTrack> StaticTracks,
        bool IncludesSubjectTrack,
        AdvancementEligibility Eligibility,
        int BaseAwardMin,
        int BaseAwardMax,
        double BaseChance,
        double ChanceDecayPerImprovement,
        double SourceScale);
}
