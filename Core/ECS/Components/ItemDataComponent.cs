using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Core data for an item entity: display name, description, keyword aliases, type
    /// classification, and optional wear slots. Cross-cutting so core systems (e.g.
    /// BroadcastSystem) can read item names without a domain dependency.
    /// </summary>
    [Persistent]
    public sealed class ItemDataComponent : IComponent
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public ItemType ItemType { get; set; } = ItemType.None;

        /// <summary>
        /// Slots this item occupies when worn. Null or empty means the item is not wearable.
        /// </summary>
        public List<WornSlot>? WornSlots { get; set; }

        /// <summary>
        /// Stat contributions applied while this item is worn, each a signed magnitude against a
        /// <see cref="Hedron.Core.Modules.Stats.ScoreId"/>. Derived on read as WhileEquipped
        /// StatModifiers by <c>EquipmentEffectContributor</c> — never written to an effect store.
        /// Empty means the item contributes no stats.
        /// </summary>
        public List<EquipmentStatBonus> StatBonuses { get; set; } = new();
    }
}
