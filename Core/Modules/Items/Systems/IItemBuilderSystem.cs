using System.Collections.Generic;
using Hedron.Core;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Items.Systems
{
    /// <summary>
    /// Domain system for runtime item authoring. <see cref="CreateItem"/> registers a template only —
    /// the world instance comes from a room spawn rule. Every <c>Set*</c> method is
    /// <b>blueprint-keyed</b> (INV-21): it updates the registered <see cref="ItemTemplate"/> and every
    /// live <b>world</b> instance of that blueprint (never a <c>PersistentEntity</c>, e.g. a
    /// player-owned copy), and returns the number of instances updated (0 is valid), or <c>-1</c> when
    /// <paramref name="blueprintId"/> names no item template. INV-5: no events, no persistence.
    /// </summary>
    public interface IItemBuilderSystem
    {
        /// <summary>Registers a new ad-hoc item template with a unique blueprint id. Creates no entity.</summary>
        ItemCreationResult CreateItem(string name);

        int SetItemName(string blueprintId, string name);
        int SetItemDescription(string blueprintId, string description);
        int SetItemKeywords(string blueprintId, IReadOnlyList<string> keywords);
        int SetItemType(string blueprintId, ItemType itemType);
        int SetItemSlots(string blueprintId, IReadOnlyList<WornSlot> slots);

        /// <summary>
        /// Add-or-replace the worn-stat bonus for <paramref name="score"/>. A <paramref name="magnitude"/>
        /// of 0 removes the row.
        /// </summary>
        int SetItemStatBonus(string blueprintId, ScoreId score, int magnitude);

        /// <summary>Remove all worn-stat bonuses.</summary>
        int ClearItemStatBonuses(string blueprintId);

        /// <summary>
        /// Sets the intrinsic base value (base-unit Coin). Pure setter — validation (non-negative
        /// guard) lives at the command edge.
        /// </summary>
        int SetItemValue(string blueprintId, long value);

        /// <summary>Sets the Ascension tier tag (<c>0</c>&#8211;<c>6</c>); callers range-validate.</summary>
        int SetItemTier(string blueprintId, int tier);

        /// <summary>Sets the descriptive Band tag (<c>0</c>&#8211;<c>3</c>); callers range-validate.</summary>
        int SetItemBand(string blueprintId, int band);
    }

    /// <summary>Result of <see cref="IItemBuilderSystem.CreateItem"/>.</summary>
    public readonly record struct ItemCreationResult(string BlueprintId, ItemTemplate Template);
}
