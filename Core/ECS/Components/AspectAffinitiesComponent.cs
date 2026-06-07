using System.Collections.Generic;
using Hedron.Core.Modules.Aspects;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Carries an entity's elemental identity/affinity and per-aspect base resistances.
    /// Both are compute-on-read: <c>IAspectSystem.Affinity</c> and <c>IAspectSystem.Resist</c>
    /// fold this component's values with any registered contributors — never cached (INV-24).
    ///
    /// Serialized by <c>AspectId</c> name (never ordinal — INV-23). Dictionary keys
    /// round-trip as strings via the <c>JsonStringEnumConverter</c> in ComponentSerializer.
    ///
    /// Attached empty to every new character by <c>AccountSystem.CreateCharacterAsync</c>
    /// so the substrate is present for inspection and authoring.
    /// </summary>
    [Persistent]
    public sealed class AspectAffinitiesComponent : IComponent
    {
        /// <summary>
        /// Normalized affinity composition: AspectId → weight (positive ints summing to 100
        /// when non-empty). Types the entity's outgoing damage and supplies the per-aspect
        /// attacker boost in <c>IAspectSystem.Resolve</c>.
        /// </summary>
        public Dictionary<AspectId, int> AffinityWeights { get; set; } = new();

        /// <summary>
        /// Independent per-aspect base resistance [0, 100]. Not derived from
        /// <see cref="AffinityWeights"/> — a separate dimension (gameplay-model R8).
        /// </summary>
        public Dictionary<AspectId, int> BaseResistances { get; set; } = new();
    }
}
