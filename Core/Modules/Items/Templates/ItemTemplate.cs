using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Items.Templates
{
    /// <summary>
    /// Authored item blueprint. Carries display name, description, keyword aliases, type,
    /// and the blueprint id of the room to spawn into at startup.
    /// </summary>
    /// <remarks>
    /// <c>Apply</c> attaches <c>ItemDataComponent</c>; <c>WorldContentLoader</c> attaches
    /// <c>LocationComponent</c> in a separate pass after rooms are resolved (so exit-linking
    /// can complete first).
    /// </remarks>
    public sealed class ItemTemplate : IEntityTemplate
    {
        public string BlueprintId { get; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public ItemType ItemType { get; set; } = ItemType.None;

        /// <summary>Slots this item occupies when worn. Empty means the item is not wearable.</summary>
        public List<WornSlot> WornSlots { get; set; } = new();

        /// <summary>Blueprint id of the room this item spawns in. Empty means no spawn location.</summary>
        public string SpawnRoomBlueprintId { get; set; } = string.Empty;

        /// <summary>Flat bonus added to the wielder's effective attack power when equipped in MainHand. Default 0.</summary>
        public int DamageBonus { get; set; }

        public ItemTemplate(string blueprintId)
        {
            BlueprintId = blueprintId;
        }

        public void Apply(Entity entity, EntityService entityService)
        {
            entityService.AddComponent(entity.Id, new ItemDataComponent
            {
                Name = Name,
                Description = Description,
                Keywords = new List<string>(Keywords),
                ItemType = ItemType,
                WornSlots = WornSlots.Count > 0 ? new List<WornSlot>(WornSlots) : null,
                DamageBonus = DamageBonus,
            });
        }
    }
}
