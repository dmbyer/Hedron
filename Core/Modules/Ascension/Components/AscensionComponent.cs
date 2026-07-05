using System.Collections.Generic;
using Hedron.Core.ECS;

namespace Hedron.Core.Modules.Ascension.Components
{
    /// <summary>
    /// Holds a player entity's character-wide Tier scalar and the ids of unlocks recorded on
    /// ascend. Attached lazily on the first successful <c>TryAscend</c> call — always a player
    /// entity already carrying <c>PersistentEntity</c> (INV-23). Tagged <c>[Persistent]</c> so a
    /// player's tier survives restart. The derived additive power baseline is <b>never</b> stored
    /// here — it is pulled on read by <see cref="Ascension.AscensionEffectContributor"/> (INV-24).
    ///
    /// <para>
    /// Mob entities never carry this component — mobs are world content and the tier-band tag
    /// lives on <c>MobDataComponent</c>/<c>MobTemplate</c> instead.
    /// </para>
    /// </summary>
    [Persistent]
    public sealed class AscensionComponent : IComponent
    {
        /// <summary>Character-wide tier, clamped to <c>[0, AscensionConstants.MaxTier]</c>.</summary>
        public int Tier { get; set; }

        /// <summary>Unlock ids recorded across every ascend so far. Empty in prog-2 (empty unlock table).</summary>
        public List<string> GrantedUnlocks { get; set; } = new();
    }
}
