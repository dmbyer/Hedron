using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Shopping.Components;

namespace Hedron.Core.Modules.Authoring.Contracts
{
    /// <summary>
    /// The mob kind's <see cref="IContentDefinitionMapper{TDto}"/>. Pure translation both ways —
    /// no validation, no id minting, no file access (see the interface remarks).
    /// </summary>
    public sealed class MobDefinitionMapper : IContentDefinitionMapper<MobDefinitionDto>
    {
        public ContentKind Kind => ContentKind.Mob;

        public MobDefinitionDto ToDto(ContentDefinition definition)
        {
            var mob = (MobTemplate)definition.Template;

            return new MobDefinitionDto
            {
                BlueprintId = mob.BlueprintId,
                Name = mob.Name,
                Description = mob.Description,
                Keywords = new List<string>(mob.Keywords),
                MobType = mob.MobType,
                SpawnRoomBlueprintId = mob.SpawnRoomBlueprintId,
                Level = mob.Level,
                MaxHp = mob.MaxHp,
                Mind = mob.Mind,
                Body = mob.Body,
                Spirit = mob.Spirit,
                Attunement = mob.Attunement,
                MaxMana = mob.MaxMana,
                MaxStamina = mob.MaxStamina,
                MaxAstra = mob.MaxAstra,
                CurrencyLoot = mob.CurrencyLoot
                    .OrderBy(kv => kv.Key)
                    .Select(kv => new CurrencyLootRowDto
                    {
                        Currency = kv.Key,
                        Min = kv.Value.Min,
                        Max = kv.Value.Max,
                    })
                    .ToList(),
                Protection = mob.Protection,
                Tier = mob.Tier,
                Band = mob.Band,
                XpScale = mob.XpScale,
                IsShop = mob.IsShop,
                ShopAcceptedCurrency = mob.ShopAcceptedCurrency,
                ShopTillSeed = mob.ShopTillSeed,
                ShopRatioOverride = mob.ShopRatioOverride,
                ShopBaseStock = mob.ShopBaseStock
                    .Select(row => new ShopStockRowDto
                    {
                        BlueprintId = row.BlueprintId,
                        Quantity = row.Quantity,
                    })
                    .ToList(),
            };
        }

        public ContentDefinition ToDefinition(MobDefinitionDto dto, string blueprintId)
        {
            var mob = new MobTemplate(blueprintId)
            {
                Name = dto.Name,
                Description = dto.Description,
                Keywords = new List<string>(dto.Keywords),
                MobType = dto.MobType,
                SpawnRoomBlueprintId = dto.SpawnRoomBlueprintId,
                Level = dto.Level,
                MaxHp = dto.MaxHp,
                Mind = dto.Mind,
                Body = dto.Body,
                Spirit = dto.Spirit,
                Attunement = dto.Attunement,
                MaxMana = dto.MaxMana,
                MaxStamina = dto.MaxStamina,
                MaxAstra = dto.MaxAstra,
                Protection = dto.Protection,
                Tier = dto.Tier,
                Band = dto.Band,
                XpScale = dto.XpScale,
                IsShop = dto.IsShop,
                ShopAcceptedCurrency = dto.ShopAcceptedCurrency,
                ShopTillSeed = dto.ShopTillSeed,
                ShopRatioOverride = dto.ShopRatioOverride,
                ShopBaseStock = dto.ShopBaseStock
                    .Select(row => new ShopStockRow
                    {
                        BlueprintId = row.BlueprintId,
                        Quantity = row.Quantity,
                    })
                    .ToList(),
            };

            // Last row wins on a duplicated currency — a translation detail, not a rule: the range
            // itself is validated (non-negative, min ≤ max) by IContentValidator on the write path.
            var loot = new Dictionary<CurrencyId, (int Min, int Max)>();
            foreach (var row in dto.CurrencyLoot)
                loot[row.Currency] = (row.Min, row.Max);
            mob.CurrencyLoot = loot;

            return new ContentDefinition(ContentKind.Mob, mob);
        }
    }
}
