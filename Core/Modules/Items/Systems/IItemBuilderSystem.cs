using System.Collections.Generic;
using Hedron.Core;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Items.Systems
{
    /// <summary>
    /// Domain system for runtime item authoring. All methods mutate entity/component state only;
    /// event publication and persistence are the caller's responsibility.
    /// Mirrors <c>IRoomBuilderSystem</c> so a future in-game editor can reuse these operations.
    /// </summary>
    public interface IItemBuilderSystem
    {
        ItemCreationResult CreateItem(string name, uint roomEntityId);
        void SetItemName(uint itemEntityId, string name);
        void SetItemDescription(uint itemEntityId, string description);
        void SetItemKeywords(uint itemEntityId, IReadOnlyList<string> keywords);
        void SetItemType(uint itemEntityId, ItemType itemType);
        void SetItemSlots(uint itemEntityId, IReadOnlyList<WornSlot> slots);

        /// <summary>
        /// Add-or-replace the worn-stat bonus for <paramref name="score"/> on the item (and its
        /// template). A <paramref name="magnitude"/> of 0 removes the row.
        /// </summary>
        void SetItemStatBonus(uint itemEntityId, ScoreId score, int magnitude);

        /// <summary>Remove all worn-stat bonuses from the item (and its template).</summary>
        void ClearItemStatBonuses(uint itemEntityId);
    }

    /// <summary>Result of <see cref="IItemBuilderSystem.CreateItem"/>.</summary>
    public readonly record struct ItemCreationResult(uint ItemEntityId, string BlueprintId, ItemTemplate Template);
}
