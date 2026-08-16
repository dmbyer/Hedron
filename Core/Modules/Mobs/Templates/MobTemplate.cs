using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Mobs.Templates
{
    public sealed class MobTemplate : IEntityTemplate
    {
        public string BlueprintId { get; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public MobType MobType { get; set; } = MobType.None;

        /// <summary>Blueprint id of the room this mob spawns in. Empty means no spawn location.</summary>
        public string SpawnRoomBlueprintId { get; set; } = string.Empty;

        public int Level { get; set; } = 0;
        public int MaxHp { get; set; } = 0;
        public int Mind { get; set; } = 0;
        public int Body { get; set; } = 0;
        public int Spirit { get; set; } = 0;
        public int Attunement { get; set; } = 0;
        public int MaxMana { get; set; } = 0;
        public int MaxStamina { get; set; } = 0;
        public int MaxAstra { get; set; } = 0;

        /// <summary>
        /// Optional per-currency loot range (min, max in base units / copper).
        /// When a currency key is absent or both min and max are zero, no loot component
        /// entry is written for that currency (opt-in default: no drop).
        /// Authored via YAML / Blazor editor and applied by <see cref="Apply"/>.
        /// </summary>
        public Dictionary<CurrencyId, (int Min, int Max)> CurrencyLoot { get; set; } = new();

        /// <summary>
        /// Optional protection flags. When <see cref="ProtectionFlags.None"/> (the default),
        /// no <see cref="ProtectionComponent"/> is added in <see cref="Apply"/> (opt-in default,
        /// mirrors the <c>CurrencyLoot</c> precedent). Durable form is YAML; NOT <c>[Persistent]</c>.
        /// </summary>
        public ProtectionFlags Protection { get; set; } = ProtectionFlags.None;

        /// <summary>
        /// Ascension tier tag, <c>0</c>&#8211;<c>6</c> (0 = unbanded/base, the default). Range-validated
        /// by <c>setmob tier</c>. Durable form is YAML (this template); <c>MobDataComponent.Tier</c>
        /// is re-applied from here on each spawn (mob entities never carry <c>PersistentEntity</c>).
        /// </summary>
        public int Tier { get; set; } = 0;

        /// <summary>
        /// Descriptive Band tag, <c>0</c>&#8211;<c>3</c> (0 = unbanded, the default; 1-3 = low/mid/high
        /// within <see cref="Tier"/>). Purely descriptive — grants no power. Range-validated by
        /// <c>setmob band</c>. Durable form is YAML; <c>MobDataComponent.Band</c> is re-applied from
        /// here on each spawn.
        /// </summary>
        public int Band { get; set; } = 0;

        /// <summary>
        /// Per-mob granular XP scale (R7) for combat-kill awards. <c>1.0</c> is the default;
        /// <c>0</c> makes this mob's kills award nothing. Validated as non-negative by
        /// <c>setmob xpscale</c>. Durable form is YAML (this template);
        /// <see cref="MobDataComponent.XpScale"/> is re-applied from here on each spawn.
        /// </summary>
        public double XpScale { get; set; } = 1.0;

        // ── Shop fields (WP-1) ───────────────────────────────────────────────────

        /// <summary>
        /// When <see langword="true"/>, <see cref="Apply"/> adds a <see cref="ShopComponent"/>
        /// and seeds the till (<see cref="WalletComponent"/>). Opt-in default: most mobs are not
        /// shopkeepers. Set via <c>IMobBuilderSystem.SetMobShop</c> or the <c>shop:</c> YAML block.
        /// </summary>
        public bool IsShop { get; set; } = false;

        /// <summary>
        /// Currency the shop accepts. Only meaningful when <see cref="IsShop"/> is <see langword="true"/>.
        /// </summary>
        public CurrencyId ShopAcceptedCurrency { get; set; } = CurrencyId.Coin;

        /// <summary>
        /// Amount deposited into the till on each spawn. 0 means defer to
        /// <c>ShopOptions.DefaultTillSeed</c> at spawn time. Only meaningful when <see cref="IsShop"/>.
        /// </summary>
        public long ShopTillSeed { get; set; } = 0;

        /// <summary>
        /// Per-shop price-ratio override (deferred — backlog; carried for authoring completeness).
        /// <see langword="null"/> = use global <c>ShopOptions</c> ratios.
        /// </summary>
        public decimal? ShopRatioOverride { get; set; } = null;

        /// <summary>
        /// Authored base-stock rows. Each entry spawns <see cref="ShopStockRow.Quantity"/> item
        /// entities from <see cref="ShopStockRow.BlueprintId"/> into the shop's inventory on startup,
        /// each stamped with <see cref="ShopStockComponent"/>&#160;<c>{ Base }</c>.
        /// Only meaningful when <see cref="IsShop"/> is <see langword="true"/>.
        /// </summary>
        public List<ShopStockRow> ShopBaseStock { get; set; } = new();

        public MobTemplate(string blueprintId)
        {
            BlueprintId = blueprintId;
        }

        public void Apply(Entity entity, EntityService entityService)
        {
            entityService.AddComponent(entity.Id, new MobDataComponent
            {
                Name = Name,
                Description = Description,
                Keywords = new List<string>(Keywords),
                MobType = MobType,
                Tier = Tier,
                Band = Band,
                XpScale = XpScale,
            });

            var level = Level > 0 ? Level : 1;
            var maxHp = MaxHp > 0 ? MaxHp : 100;
            entityService.AddComponent(entity.Id, new AttributesComponent
            {
                Level = level,
                Mind = Mind > 0 ? Mind : 10,
                Body = Body > 0 ? Body : 10,
                Spirit = Spirit > 0 ? Spirit : 10,
                Attunement = Attunement > 0 ? Attunement : 10,
            });
            var maxMana = MaxMana > 0 ? MaxMana : 50;
            var maxStamina = MaxStamina > 0 ? MaxStamina : 50;
            var maxAstra = MaxAstra > 0 ? MaxAstra : 10;
            entityService.AddComponent(entity.Id, new PoolsComponent
            {
                MaxHp = maxHp,
                CurrentHp = maxHp,
                MaxMana = maxMana,
                CurrentMana = maxMana,
                MaxStamina = maxStamina,
                CurrentStamina = maxStamina,
                MaxAstra = maxAstra,
                CurrentAstra = maxAstra,
            });

            // Add CurrencyLootComponent only when at least one non-zero range is configured.
            // Zero / absent range → no component → no drop (opt-in default, INV-23 world content).
            var lootComp = new CurrencyLootComponent();
            foreach (var (currency, range) in CurrencyLoot)
            {
                if (range.Max > 0)
                    lootComp.Ranges[currency] = range;
            }
            if (lootComp.Ranges.Count > 0)
                entityService.AddComponent(entity.Id, lootComp);

            // Add ProtectionComponent only when flags are non-None (opt-in default, mirrors CurrencyLoot).
            // ProtectionFlags.None → no component → no protection (world-content default).
            if (Protection != ProtectionFlags.None)
                entityService.AddComponent(entity.Id, new ProtectionComponent { Flags = Protection });

            // Add ShopComponent + InventoryComponent only when this mob is a shopkeeper (opt-in default).
            // Till seeding and base-stock entity spawning are deferred to ShopkeeperSpawnHandler (the
            // WorldContentReadyEvent second pass, analogous to how items receive LocationComponent after
            // load), because those steps require IShopSystem, ITemplateRegistry, and ShopOptions — none of
            // which are available in Apply. ShopComponent carries all authored values needed by that pass.
            if (IsShop)
            {
                entityService.AddComponent(entity.Id, new ShopComponent
                {
                    AcceptedCurrency = ShopAcceptedCurrency,
                    TillSeed = ShopTillSeed,
                    RatioOverride = ShopRatioOverride,
                    BaseStock = new List<ShopStockRow>(ShopBaseStock),
                });
                // Ensure the shopkeeper has an inventory to hold stock.
                if (!entityService.HasComponent<InventoryComponent>(entity.Id))
                    entityService.AddComponent(entity.Id, new InventoryComponent());
            }
        }
    }
}
