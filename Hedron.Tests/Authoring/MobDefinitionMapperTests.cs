using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Contracts;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Shopping.Components;
using Xunit;

namespace Hedron.Tests.Authoring
{
    /// <summary>
    /// Tier 1 — the mob kind's DTO mapping (authoring-api-surface WP2). The mapping is a real
    /// surface, not endpoint plumbing: <c>ContentDefinition</c> has no parameterless constructor, a
    /// get-only derived id, and a polymorphic template, so it cannot round-trip unmapped. A field
    /// silently dropped here is a field silently dropped from every out-of-process write.
    /// </summary>
    public sealed class MobDefinitionMapperTests
    {
        private static readonly MobDefinitionMapper Mapper = new();

        private static MobTemplate FullyPopulated(string blueprintId = "mob.source") =>
            new(blueprintId)
            {
                Name = "Cave Troll",
                Description = "Enormous and slow.",
                Keywords = new List<string> { "troll", "cave" },
                MobType = MobType.Guard,
                SpawnRoomBlueprintId = "room.cavern",
                Level = 7,
                MaxHp = 450,
                Mind = 4,
                Body = 26,
                Spirit = 6,
                Attunement = 3,
                MaxMana = 10,
                MaxStamina = 90,
                MaxAstra = 5,
                CurrencyLoot = new Dictionary<CurrencyId, (int Min, int Max)>
                {
                    [CurrencyId.Coin] = (40, 120),
                },
                Protection = ProtectionFlags.Untargetable | ProtectionFlags.EffectImmune,
                Tier = 3,
                Band = 2,
                XpScale = 2.25,
                IsShop = true,
                ShopAcceptedCurrency = CurrencyId.Coin,
                ShopTillSeed = 7500,
                ShopRatioOverride = 1.35m,
                ShopBaseStock = new List<ShopStockRow>
                {
                    new() { BlueprintId = "item.club", Quantity = 1 },
                    new() { BlueprintId = "item.hide", Quantity = 4 },
                },
            };

        [Fact]
        public void Kind_is_mob()
        {
            Assert.Equal(ContentKind.Mob, Mapper.Kind);
        }

        [Fact]
        public void Every_authored_field_survives_a_full_round_trip()
        {
            var source = FullyPopulated();

            var dto = Mapper.ToDto(new ContentDefinition(ContentKind.Mob, source));
            var result = (MobTemplate)Mapper.ToDefinition(dto, source.BlueprintId).Template;

            Assert.Equal(source.BlueprintId, result.BlueprintId);
            Assert.Equal(source.Name, result.Name);
            Assert.Equal(source.Description, result.Description);
            Assert.Equal(source.Keywords, result.Keywords);
            Assert.Equal(source.MobType, result.MobType);
            Assert.Equal(source.SpawnRoomBlueprintId, result.SpawnRoomBlueprintId);
            Assert.Equal(source.Level, result.Level);
            Assert.Equal(source.MaxHp, result.MaxHp);
            Assert.Equal(source.Mind, result.Mind);
            Assert.Equal(source.Body, result.Body);
            Assert.Equal(source.Spirit, result.Spirit);
            Assert.Equal(source.Attunement, result.Attunement);
            Assert.Equal(source.MaxMana, result.MaxMana);
            Assert.Equal(source.MaxStamina, result.MaxStamina);
            Assert.Equal(source.MaxAstra, result.MaxAstra);
            Assert.Equal(source.CurrencyLoot, result.CurrencyLoot);
            Assert.Equal(source.Protection, result.Protection);
            Assert.Equal(source.Tier, result.Tier);
            Assert.Equal(source.Band, result.Band);
            Assert.Equal(source.XpScale, result.XpScale);
            Assert.Equal(source.IsShop, result.IsShop);
            Assert.Equal(source.ShopAcceptedCurrency, result.ShopAcceptedCurrency);
            Assert.Equal(source.ShopTillSeed, result.ShopTillSeed);
            Assert.Equal(source.ShopRatioOverride, result.ShopRatioOverride);
            Assert.Equal(
                source.ShopBaseStock.Select(r => (r.BlueprintId, r.Quantity)),
                result.ShopBaseStock.Select(r => (r.BlueprintId, r.Quantity)));
        }

        [Fact]
        public void Every_settable_template_property_is_covered_by_the_round_trip()
        {
            // Guards the guard: a new authored field on MobTemplate that the mapper forgets would
            // otherwise pass the assertions above simply by not being mentioned in them.
            var source = FullyPopulated();
            var result = (MobTemplate)Mapper
                .ToDefinition(Mapper.ToDto(new ContentDefinition(ContentKind.Mob, source)), source.BlueprintId)
                .Template;

            var unmapped = typeof(MobTemplate)
                .GetProperties()
                .Where(p => p.CanRead && p.CanWrite)
                .Where(p => !Equals(
                    Normalize(p.GetValue(source)),
                    Normalize(p.GetValue(result))))
                .Select(p => p.Name)
                .ToList();

            Assert.True(
                unmapped.Count == 0,
                "MobDefinitionMapper drops these MobTemplate properties: " + string.Join(", ", unmapped));

            static object? Normalize(object? value) => value switch
            {
                List<string> strings => string.Join("|", strings),
                List<ShopStockRow> rows => string.Join("|", rows.Select(r => $"{r.BlueprintId}:{r.Quantity}")),
                Dictionary<CurrencyId, (int Min, int Max)> loot =>
                    string.Join("|", loot.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value.Min}-{kv.Value.Max}")),
                _ => value,
            };
        }

        [Fact]
        public void The_caller_supplied_id_wins_over_the_dtos_own()
        {
            var dto = Mapper.ToDto(new ContentDefinition(ContentKind.Mob, FullyPopulated("mob.from-body")));

            var result = Mapper.ToDefinition(dto, "mob.from-route");

            Assert.Equal("mob.from-route", result.BlueprintId);
        }

        [Fact]
        public void The_mapper_copies_collections_rather_than_aliasing_them()
        {
            var source = FullyPopulated();
            var dto = Mapper.ToDto(new ContentDefinition(ContentKind.Mob, source));

            dto.Keywords.Add("mutated");
            dto.ShopBaseStock.Clear();

            Assert.DoesNotContain("mutated", source.Keywords);
            Assert.Equal(2, source.ShopBaseStock.Count);
        }

        [Fact]
        public void A_duplicated_currency_row_resolves_to_the_last_one()
        {
            var dto = new MobDefinitionDto
            {
                Name = "Dupe",
                CurrencyLoot = new List<CurrencyLootRowDto>
                {
                    new() { Currency = CurrencyId.Coin, Min = 1, Max = 2 },
                    new() { Currency = CurrencyId.Coin, Min = 30, Max = 40 },
                },
            };

            var result = (MobTemplate)Mapper.ToDefinition(dto, "mob.dupe").Template;

            Assert.Equal((30, 40), Assert.Single(result.CurrencyLoot).Value);
        }

        [Fact]
        public void An_empty_dto_maps_to_a_template_carrying_only_the_id()
        {
            var result = (MobTemplate)Mapper.ToDefinition(new MobDefinitionDto(), "mob.blank").Template;

            Assert.Equal("mob.blank", result.BlueprintId);
            Assert.Empty(result.CurrencyLoot);
            Assert.Empty(result.ShopBaseStock);
            Assert.Equal(ProtectionFlags.None, result.Protection);
        }
    }
}
