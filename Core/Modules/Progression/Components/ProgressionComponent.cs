using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Progression.Components
{
    /// <summary>
    /// Holds an entity's per-track cumulative experience and improvement counts, keyed directly
    /// by <see cref="ScoreId"/> (a track is a score — no parallel key type). Absent keys mean the
    /// track has never been awarded.
    ///
    /// <para>
    /// Attached lazily to an entity's first <c>AwardExperience</c> call — always a player entity
    /// already carrying <c>PersistentEntity</c> (INV-23). Tagged <c>[Persistent]</c> so a player's
    /// progression survives restart. The derived power step is <b>never</b> stored here — it is
    /// pulled on read by <see cref="Systems.ProgressionEffectContributor"/> (INV-24).
    /// </para>
    ///
    /// <para>
    /// Dictionary keys are serialized by enum name (not ordinal) because <c>ComponentSerializer</c>
    /// uses <c>JsonStringEnumConverter</c> globally — mirrors <c>WalletComponent</c>.
    /// </para>
    /// </summary>
    [Persistent]
    public sealed class ProgressionComponent : IComponent
    {
        /// <summary>Cumulative experience ever earned per track. Never decremented.</summary>
        public Dictionary<ScoreId, int> Xp { get; set; } = new();

        /// <summary>Number of thresholds crossed per track — the power-step count.</summary>
        public Dictionary<ScoreId, int> Improvements { get; set; } = new();
    }
}
