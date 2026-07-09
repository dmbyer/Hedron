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

        /// <summary>Stat contributions applied while this item is worn (WhileEquipped StatModifiers, derived on read). Empty = none.</summary>
        public List<EquipmentStatBonus> StatBonuses { get; set; } = new();

        /// <summary>
        /// Intrinsic base value in base-unit Coin. <c>0</c> means "valueless / not saleable".
        /// Copied onto <see cref="ItemDataComponent.Value"/> by <see cref="Apply"/>.
        /// </summary>
        public long Value { get; set; } = 0;

        /// <summary>
        /// Optional Ascension tier tag (0-6). 0 = unbanded/base (default). Copied onto
        /// <see cref="ItemDataComponent.Tier"/> by <see cref="Apply"/>.
        /// </summary>
        public int Tier { get; set; }

        /// <summary>
        /// Optional descriptive Band tag (0-3). 0 = unbanded (default), 1-3 = low/mid/high within
        /// <see cref="Tier"/>. Purely descriptive. Copied onto <see cref="ItemDataComponent.Band"/>
        /// by <see cref="Apply"/>.
        /// </summary>
        public int Band { get; set; }

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
                StatBonuses = new List<EquipmentStatBonus>(StatBonuses),
                Value = Value,
                Tier = Tier,
                Band = Band,
            });
        }
    }
}
