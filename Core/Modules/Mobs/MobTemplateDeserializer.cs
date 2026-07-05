using System;
using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.Mobs
{
    public sealed class MobTemplateDeserializer : ITemplateDeserializer
    {
        private readonly ILogger<MobTemplateDeserializer> _logger;
        private readonly IDeserializer _yaml;

        public string Kind => "mob";

        public MobTemplateDeserializer(ILogger<MobTemplateDeserializer> logger)
        {
            _logger = logger;
            _yaml = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }

        public IEntityTemplate Deserialize(string fileBody)
        {
            var dto = _yaml.Deserialize<MobDto>(fileBody)
                ?? throw new InvalidOperationException("Empty mob YAML.");

            if (string.IsNullOrWhiteSpace(dto.BlueprintId))
                throw new InvalidOperationException("Mob file is missing required 'blueprintId' field.");

            var template = new MobTemplate(dto.BlueprintId)
            {
                Name = dto.Name ?? string.Empty,
                Description = dto.Description ?? string.Empty,
                SpawnRoomBlueprintId = dto.SpawnRoomBlueprintId ?? string.Empty,
            };

            if (dto.Keywords is { Count: > 0 })
                template.Keywords.AddRange(dto.Keywords);

            if (!string.IsNullOrEmpty(dto.Type) &&
                Enum.TryParse<MobType>(dto.Type, ignoreCase: true, out var mobType))
                template.MobType = mobType;
            else if (!string.IsNullOrEmpty(dto.Type))
                _logger.LogWarning(
                    "Mob '{Id}': unknown type '{Type}' — defaulting to None.",
                    dto.BlueprintId, dto.Type);

            template.Level = dto.Level;
            template.MaxHp = dto.MaxHp;
            template.Mind = dto.Mind;
            template.Body = dto.Body;
            template.Spirit = dto.Spirit;
            template.Attunement = dto.Attunement;
            template.MaxMana = dto.MaxMana;
            template.MaxStamina = dto.MaxStamina;
            template.MaxAstra = dto.MaxAstra;

            // Deserialize currency loot ranges. Keys are CurrencyId enum names (case-insensitive).
            // Unknown currency names are logged and skipped so a stale YAML key doesn't crash startup.
            if (dto.CurrencyLoot is { Count: > 0 })
            {
                foreach (var (key, rangeDto) in dto.CurrencyLoot)
                {
                    if (Enum.TryParse<CurrencyId>(key, ignoreCase: true, out var currencyId))
                    {
                        if (rangeDto.Max > 0)
                            template.CurrencyLoot[currencyId] = (rangeDto.Min, rangeDto.Max);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Mob '{Id}': unknown currencyLoot key '{Key}' — skipping.",
                            dto.BlueprintId, key);
                    }
                }
            }

            // Deserialize protection flags. Each list entry is a ProtectionFlags enum name (case-insensitive).
            // Unknown flag names are logged and skipped so a stale YAML entry doesn't crash startup.
            if (dto.Protection is { Count: > 0 })
            {
                var combined = ProtectionFlags.None;
                foreach (var flagName in dto.Protection)
                {
                    if (Enum.TryParse<ProtectionFlags>(flagName, ignoreCase: true, out var flag) &&
                        flag != ProtectionFlags.None)
                    {
                        combined |= flag;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Mob '{Id}': unknown protection flag '{Flag}' — skipping.",
                            dto.BlueprintId, flagName);
                    }
                }
                template.Protection = combined;
            }

            // Deserialize the tier-band tag. Null/absent → 0 (unbanded, the default).
            if (dto.Band.HasValue)
            {
                if (dto.Band.Value is >= 0 and <= 6)
                {
                    template.TierBand = dto.Band.Value;
                }
                else
                {
                    _logger.LogWarning(
                        "Mob '{Id}': band '{Band}' out of range 0-6 — defaulting to 0 (unbanded).",
                        dto.BlueprintId, dto.Band.Value);
                }
            }

            // Deserialize shop block. Null / absent → not a shopkeeper (opt-in default).
            if (dto.Shop is not null)
            {
                template.IsShop = true;

                if (!string.IsNullOrEmpty(dto.Shop.AcceptedCurrency) &&
                    Enum.TryParse<CurrencyId>(dto.Shop.AcceptedCurrency, ignoreCase: true, out var currency))
                {
                    template.ShopAcceptedCurrency = currency;
                }
                else if (!string.IsNullOrEmpty(dto.Shop.AcceptedCurrency))
                {
                    _logger.LogWarning(
                        "Mob '{Id}': unknown shop currency '{Currency}' — defaulting to Coin.",
                        dto.BlueprintId, dto.Shop.AcceptedCurrency);
                }

                template.ShopTillSeed = dto.Shop.TillSeed;
                template.ShopRatioOverride = dto.Shop.RatioOverride;

                if (dto.Shop.BaseStock is { Count: > 0 })
                {
                    foreach (var row in dto.Shop.BaseStock)
                    {
                        if (string.IsNullOrWhiteSpace(row.BlueprintId))
                            continue;
                        template.ShopBaseStock.Add(new ShopStockRow
                        {
                            BlueprintId = row.BlueprintId,
                            Quantity = row.Quantity > 0 ? row.Quantity : 1,
                        });
                    }
                }
            }

            return template;
        }

        private sealed class CurrencyLootRangeDto
        {
            public int Min { get; set; }
            public int Max { get; set; }
        }

        private sealed class MobDto
        {
            public string? BlueprintId { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public List<string>? Keywords { get; set; }
            public string? Type { get; set; }
            public string? SpawnRoomBlueprintId { get; set; }
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
            /// Optional list of <see cref="ProtectionFlags"/> enum names (e.g. ["Untargetable", "EffectImmune"]).
            /// Null / absent means no protection (opt-in default).
            /// </summary>
            public List<string>? Protection { get; set; }
            /// <summary>
            /// Optional Ascension tier-band tag (0-6). Null / absent means unbanded (0), the default.
            /// </summary>
            public int? Band { get; set; }
            /// <summary>
            /// Optional shop configuration block. Null / absent means this mob is not a shopkeeper.
            /// </summary>
            public ShopDto? Shop { get; set; }
        }

        private sealed class ShopDto
        {
            public string? AcceptedCurrency { get; set; }
            public long TillSeed { get; set; }
            public decimal? RatioOverride { get; set; }
            public List<ShopStockRowDto>? BaseStock { get; set; }
        }

        private sealed class ShopStockRowDto
        {
            public string? BlueprintId { get; set; }
            public int Quantity { get; set; } = 1;
        }
    }
}
