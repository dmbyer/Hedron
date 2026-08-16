using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.World;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Mobs.Systems
{
    public sealed class MobContentWriter : IMobContentWriter
    {
        private readonly string _mobsDirectory;
        private readonly ISerializer _yaml;

        public MobContentWriter(IOptions<WorldOptions> options)
        {
            _mobsDirectory = Path.Combine(options.Value.ContentDirectory, "mobs");
            _yaml = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        public async Task WriteAsync(MobTemplate template, CancellationToken ct = default)
        {
            Directory.CreateDirectory(_mobsDirectory);

            // Map currency loot ranges to the DTO shape: Dictionary<string, CurrencyLootRangeDto>
            // keyed by enum name (not ordinal) so YAML files are stable under CurrencyId reordering.
            var currencyLoot = new Dictionary<string, CurrencyLootRangeDto>();
            foreach (var (currency, range) in template.CurrencyLoot)
            {
                if (range.Max > 0)
                    currencyLoot[currency.ToString()] = new CurrencyLootRangeDto
                    {
                        Min = range.Min,
                        Max = range.Max,
                    };
            }

            // Serialize protection flags as a list of individual flag names (e.g. ["Untargetable", "EffectImmune"]).
            // None/empty → null → absent from YAML (opt-in default).
            List<string>? protectionFlags = null;
            if (template.Protection != ProtectionFlags.None)
            {
                protectionFlags = Enum.GetValues<ProtectionFlags>()
                    .Where(f => f != ProtectionFlags.None && template.Protection.HasFlag(f))
                    .Select(f => f.ToString())
                    .ToList();
            }

            // Serialize shop block when the mob is a shopkeeper.
            ShopDto? shopDto = null;
            if (template.IsShop)
            {
                var stockRows = template.ShopBaseStock
                    .Where(r => !string.IsNullOrEmpty(r.BlueprintId) && r.Quantity > 0)
                    .Select(r => new ShopStockRowDto { BlueprintId = r.BlueprintId, Quantity = r.Quantity })
                    .ToList();

                shopDto = new ShopDto
                {
                    AcceptedCurrency = template.ShopAcceptedCurrency.ToString(),
                    TillSeed = template.ShopTillSeed,
                    RatioOverride = template.ShopRatioOverride,
                    BaseStock = stockRows.Count > 0 ? stockRows : null,
                };
            }

            var dto = new MobDto
            {
                BlueprintId = template.BlueprintId,
                Name = template.Name,
                Description = template.Description,
                Keywords = new List<string>(template.Keywords),
                Type = template.MobType.ToString(),
                SpawnRoomBlueprintId = template.SpawnRoomBlueprintId,
                Level = template.Level,
                MaxHp = template.MaxHp,
                Mind = template.Mind,
                Body = template.Body,
                Spirit = template.Spirit,
                Attunement = template.Attunement,
                MaxMana = template.MaxMana,
                MaxStamina = template.MaxStamina,
                MaxAstra = template.MaxAstra,
                CurrencyLoot = currencyLoot.Count > 0 ? currencyLoot : null,
                Protection = protectionFlags,
                Tier = template.Tier != 0 ? template.Tier : null,
                Band = template.Band != 0 ? template.Band : null,
                XpScale = template.XpScale != 1.0 ? template.XpScale : null,
                Shop = shopDto,
            };

            var body = _yaml.Serialize(dto);
            var filePath = Path.Combine(_mobsDirectory, $"{template.BlueprintId}.yaml");

            await AtomicFileWrite.ReplaceAsync(filePath, body, ct).ConfigureAwait(false);
        }

        private sealed class CurrencyLootRangeDto
        {
            public int Min { get; set; }
            public int Max { get; set; }
        }

        private sealed class MobDto
        {
            public string BlueprintId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<string> Keywords { get; set; } = new();
            public string Type { get; set; } = string.Empty;
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
            /// <summary>
            /// Optional per-currency loot range. Key is the <see cref="CurrencyId"/> enum name.
            /// Null / absent means no loot ranges configured (no drop by default).
            /// </summary>
            public Dictionary<string, CurrencyLootRangeDto>? CurrencyLoot { get; set; }
            /// <summary>
            /// Optional protection flag names (e.g. ["Untargetable", "EffectImmune"]).
            /// Null / absent means no protection (opt-in default).
            /// </summary>
            public List<string>? Protection { get; set; }
            /// <summary>
            /// Optional Ascension tier tag (0-6). Null / absent means unbanded/base (0), the default.
            /// </summary>
            public int? Tier { get; set; }
            /// <summary>
            /// Optional descriptive Band tag (0-3). Null / absent means unbanded (0), the default.
            /// </summary>
            public int? Band { get; set; }
            /// <summary>
            /// Optional per-mob XP scale for combat-kill awards. Null / absent means 1.0 (the default).
            /// </summary>
            public double? XpScale { get; set; }
            /// <summary>
            /// Optional shop configuration block. Null / absent means this mob is not a shopkeeper.
            /// </summary>
            public ShopDto? Shop { get; set; }
        }

        private sealed class ShopDto
        {
            /// <summary><see cref="CurrencyId"/> enum name (e.g. "Coin").</summary>
            public string AcceptedCurrency { get; set; } = "Coin";
            /// <summary>Till seed in base units. 0 = use global <c>ShopOptions.DefaultTillSeed</c>.</summary>
            public long TillSeed { get; set; } = 0;
            /// <summary>Optional per-shop price-ratio override (deferred backlog). Null = use global ratios.</summary>
            public decimal? RatioOverride { get; set; } = null;
            /// <summary>Authored base-stock rows. Null / absent = no base stock.</summary>
            public List<ShopStockRowDto>? BaseStock { get; set; }
        }

        private sealed class ShopStockRowDto
        {
            public string BlueprintId { get; set; } = string.Empty;
            public int Quantity { get; set; } = 1;
        }
    }
}
