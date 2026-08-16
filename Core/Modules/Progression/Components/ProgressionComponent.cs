using System.Collections.Generic;
using Hedron.Core.ECS;

namespace Hedron.Core.Modules.Progression.Components
{
    /// <summary>
    /// Holds an entity's per-track cumulative experience and improvement counts, keyed by
    /// <see cref="ProgressionTrack"/> — one vocabulary spanning score tracks (attributes/pools)
    /// and ability tracks, over a single improvement engine. Absent keys mean the track has never
    /// been awarded.
    ///
    /// <para>
    /// Attached lazily to an entity's first <c>AwardExperience</c> call — always a player entity
    /// already carrying <c>PersistentEntity</c> (INV-23). Tagged <c>[Persistent]</c> so a player's
    /// progression survives restart. The derived power step is <b>never</b> stored here — it is
    /// pulled on read by <see cref="Systems.ProgressionEffectContributor"/> (INV-24).
    /// </para>
    ///
    /// <para>
    /// Dictionary keys are serialized by <see cref="ProgressionTrack.ToKey"/> via
    /// <see cref="ProgressionTrackJsonConverter"/>. A score track renders as the bare enum name,
    /// exactly what the pre-widening <c>Dictionary&lt;ScoreId, int&gt;</c> emitted, so pre-slice
    /// snapshots round-trip byte-identically and no migration is needed. Ability tracks take the
    /// reserved <c>ability:</c> prefix.
    /// </para>
    /// </summary>
    [Persistent]
    public sealed class ProgressionComponent : IComponent
    {
        /// <summary>Cumulative experience ever earned per track. Never decremented.</summary>
        public Dictionary<ProgressionTrack, int> Xp { get; set; } = new();

        /// <summary>
        /// Number of thresholds crossed per track. For a score track this is the power-step count;
        /// for an ability track it is the displayed rank, which grants no power (D3).
        /// </summary>
        public Dictionary<ProgressionTrack, int> Improvements { get; set; } = new();
    }
}
