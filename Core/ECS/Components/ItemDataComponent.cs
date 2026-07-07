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

        /// <summary>
        /// Intrinsic base value of this item in base-unit Coin (the <c>CurrencyRegistry</c> base
        /// unit). <c>0</c> means "valueless / not saleable". Prices are derived from this at
        /// read time by consumers (e.g. the shop system) — never stored separately.
        /// </summary>
        public long Value { get; set; } = 0;

        /// <summary>
        /// Ascension tier-band tag, <c>0</c>&#8211;<c>6</c> (0 = unbanded). Authored on
        /// <c>ItemTemplate</c>/YAML, mirroring <c>MobDataComponent.TierBand</c>; used only for the
        /// authored-vs-computed comparison in the <c>power</c> inspector / Blazor readout — not a
        /// power multiplier. World-spawn items never carry <c>PersistentEntity</c>, so despite
        /// this component being <c>[Persistent]</c>, the band never reaches a snapshot for world
        /// content; its durable form is the YAML template, re-applied on each spawn.
        /// </summary>
        public int TierBand { get; set; }
    }
}
