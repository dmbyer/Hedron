namespace Hedron.Core.Modules.Progression.Systems
{
    /// <summary>
    /// One relative-power reading: the subject's and target's effective power, the
    /// target ÷ subject <see cref="Ratio"/>, and its <see cref="Label"/>.
    /// An unreadable reading (<see cref="IsReadable"/> false) has <c>Ratio == 0.0</c> and a
    /// <see langword="null"/> <c>Label</c> — at <c>0.0</c> the anti-grind floor makes any award
    /// ineligible, so a caller can pass <c>Ratio</c> through verbatim.
    /// </summary>
    public readonly record struct RelativePowerReading(
        bool IsReadable,
        int SubjectPower,
        int TargetPower,
        double Ratio,
        RelativePowerLabel? Label)
    {
        /// <summary>The single unreadable shape: no powers, ratio <c>0.0</c>, no label.</summary>
        public static RelativePowerReading Unreadable { get; } = new(false, 0, 0, 0.0, null);
    }

    /// <summary>
    /// The shared relative-power read (Progression module, domain tier): how strong a target is
    /// relative to a subject, on <b>effective</b> power for both sides — every score through
    /// <c>IStatSystem.Get</c> (gear, progression, effects, a player's tier) estimated by the core
    /// <c>IPowerBudgetSystem</c> oracle. Consumed by <c>power</c>'s live readout and relation line,
    /// and (later in this slice) <c>consider</c> and anti-grind gathering, so all three measure an
    /// entity the same way (INV-19). Owns the ratio → label rule (INV-1/INV-8); what the ratio
    /// does to an award stays in <c>ProgressionSystem</c>, which must never depend on this read.
    ///
    /// <para>
    /// <b>Readability is explicit.</b> <c>IStatSystem</c> returns reference defaults for an entity
    /// with no components, so a destroyed opponent would otherwise read as a fresh build. A reading
    /// is unreadable when either entity lacks <c>AttributesComponent</c>, or when subject power is
    /// <c>&lt;= 0</c> — never a divide-by-zero, never a plausible-looking ratio.
    /// </para>
    ///
    /// Stateless and pure: no <c>IRandom</c>/<c>IClock</c>, publishes nothing (INV-5, INV-26).
    /// </summary>
    public interface IRelativePowerSystem
    {
        /// <summary>
        /// Effective power of a live entity: every <c>ScoreId</c> via <c>IStatSystem.Get</c>,
        /// estimated with tier = <c>MobDataComponent.Tier</c> (mobs) or 0 (players — their tier is
        /// already folded in by the ascension contributor). <b>Ungated</b> — this is <c>power</c>'s
        /// readout; never use it for a ratio without <see cref="Relate(uint, uint)"/>'s readability check.
        /// </summary>
        int GetEffectivePower(uint entityId);

        /// <summary>
        /// Target ÷ subject on effective power. Unreadable (either side lacks
        /// <c>AttributesComponent</c>, or subject power <c>&lt;= 0</c>) ⇒
        /// <see cref="RelativePowerReading.Unreadable"/>.
        /// </summary>
        RelativePowerReading Relate(uint subjectEntityId, uint targetEntityId);

        /// <summary>
        /// Power-keyed overload — the entity overload delegates here once it has the target's
        /// effective power. Shaped for later consumers that compare against a cell anchor rather
        /// than an entity. A negative <paramref name="targetPower"/> clamps to 0.
        /// </summary>
        RelativePowerReading Relate(uint subjectEntityId, int targetPower);

        /// <summary>
        /// Ratio → label, inclusive lower bounds: <c>&lt; AntiGrindFloorRatio</c> Trivial;
        /// <c>&lt; RelativeEvenFloorRatio</c> Easy; <c>&lt; RelativeDangerousFloorRatio</c> Even;
        /// <c>&lt; RelativeDeadlyFloorRatio</c> Dangerous; else Deadly.
        /// </summary>
        RelativePowerLabel Classify(double ratio);
    }
}
