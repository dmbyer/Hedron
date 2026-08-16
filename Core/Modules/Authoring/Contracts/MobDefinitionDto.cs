using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;

namespace Hedron.Core.Modules.Authoring.Contracts
{
    /// <summary>One authored currency-loot range, in base units (copper).</summary>
    /// <remarks>
    /// The template stores these as a <c>Dictionary&lt;CurrencyId, (int Min, int Max)&gt;</c>. A
    /// value tuple has fields rather than properties and would serialize as <c>{}</c>, and an
    /// enum-keyed dictionary has no stable JSON key form, so the transport shape is a list of
    /// explicit rows.
    /// </remarks>
    public sealed class CurrencyLootRowDto
    {
        public CurrencyId Currency { get; set; } = CurrencyId.Coin;
        public int Min { get; set; }
        public int Max { get; set; }
    }

    /// <summary>One authored shop base-stock row.</summary>
    public sealed class ShopStockRowDto
    {
        public string BlueprintId { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
    }

    /// <summary>
    /// Transport shape for a mob definition — a flat mirror of <c>MobTemplate</c>'s authored fields,
    /// carrying no behavior. Translated by <see cref="MobDefinitionMapper"/>; every rule about what
    /// is a <em>legal</em> mob stays in <c>IContentValidator</c> / <c>IContentDefinitionCatalog</c>.
    /// </summary>
    /// <remarks>
    /// Renaming a property here is a breaking contract change and is caught by the OpenAPI drift
    /// gate. Renaming a property on <c>MobTemplate</c> is caught earlier, by the compiler, at the
    /// mapper.
    /// </remarks>
    public sealed class MobDefinitionDto
    {
        /// <summary>
        /// The definition's blueprint id. Server-authoritative: it is echoed on reads and ignored on
        /// writes, where the id comes from the route (update) or is chosen/minted at create time.
        /// </summary>
        public string BlueprintId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public MobType MobType { get; set; } = MobType.None;

        /// <summary>Blueprint id of the room this mob spawns in; empty means no spawn location.</summary>
        public string SpawnRoomBlueprintId { get; set; } = string.Empty;

        public int Level { get; set; }
        public int MaxHp { get; set; }
        public int Mind { get; set; }
        public int Body { get; set; }
        public int Spirit { get; set; }
        public int Attunement { get; set; }
        public int MaxMana { get; set; }
        public int MaxStamina { get; set; }
        public int MaxAstra { get; set; }

        public List<CurrencyLootRowDto> CurrencyLoot { get; set; } = new();
        public ProtectionFlags Protection { get; set; } = ProtectionFlags.None;

        public int Tier { get; set; }
        public int Band { get; set; }
        public double XpScale { get; set; } = 1.0;

        public bool IsShop { get; set; }
        public CurrencyId ShopAcceptedCurrency { get; set; } = CurrencyId.Coin;
        public long ShopTillSeed { get; set; }
        public decimal? ShopRatioOverride { get; set; }
        public List<ShopStockRowDto> ShopBaseStock { get; set; } = new();
    }
}
