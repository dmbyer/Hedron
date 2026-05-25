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
    }
}
