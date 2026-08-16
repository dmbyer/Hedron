using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Mobs.Systems
{
    public sealed class MobBuilderSystem : IMobBuilderSystem
    {
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly ILogger<MobBuilderSystem> _logger;

        public MobBuilderSystem(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            ILogger<MobBuilderSystem> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _logger = logger;
        }

        public MobCreationResult CreateMob(string name, uint roomEntityId)
        {
            var blueprintId = GenerateUniqueBlueprintId();

            var spawnRoomBlueprintId = string.Empty;
            if (_entityService.TryGet<BlueprintComponent>(roomEntityId, out var roomBp))
                spawnRoomBlueprintId = roomBp.BlueprintId;

            var entity = _entityService.CreateEntity();
            _entityService.AddComponent(entity.Id, new MobDataComponent { Name = name });
            _entityService.AddComponent(entity.Id, new BlueprintComponent { BlueprintId = blueprintId });
            _entityService.AddComponent(entity.Id, new LocationComponent
            {
                RoomEntityId = roomEntityId,
                RoomBlueprintId = spawnRoomBlueprintId,
            });
            _entityService.AddComponent(entity.Id, new AttributesComponent());
            _entityService.AddComponent(entity.Id, new PoolsComponent());

            var template = new MobTemplate(blueprintId)
            {
                Name = name,
                SpawnRoomBlueprintId = spawnRoomBlueprintId,
            };
            _templateRegistry.Register(blueprintId, template);

            _logger.LogDebug(
                "MobBuilderSystem: created mob entity={EntityId} blueprint={BlueprintId} spawnRoom={SpawnRoom}",
                entity.Id, blueprintId, spawnRoomBlueprintId);

            return new MobCreationResult(entity.Id, blueprintId, template);
        }

        public void SetMobName(uint mobEntityId, string name)
        {
            if (_entityService.TryGet<MobDataComponent>(mobEntityId, out var mob))
                mob.Name = name;
            var tpl = TryGetTemplate(mobEntityId);
            if (tpl is not null) tpl.Name = name;
        }

        public void SetMobDescription(uint mobEntityId, string description)
        {
            if (_entityService.TryGet<MobDataComponent>(mobEntityId, out var mob))
                mob.Description = description;
            var tpl = TryGetTemplate(mobEntityId);
            if (tpl is not null) tpl.Description = description;
        }

        public void SetMobKeywords(uint mobEntityId, IReadOnlyList<string> keywords)
        {
            if (_entityService.TryGet<MobDataComponent>(mobEntityId, out var mob))
            {
                mob.Keywords.Clear();
                mob.Keywords.AddRange(keywords);
            }
            var tpl = TryGetTemplate(mobEntityId);
            if (tpl is not null)
            {
                tpl.Keywords.Clear();
                tpl.Keywords.AddRange(keywords);
            }
        }

        public void SetMobType(uint mobEntityId, MobType mobType)
        {
            if (_entityService.TryGet<MobDataComponent>(mobEntityId, out var mob))
                mob.MobType = mobType;
            var tpl = TryGetTemplate(mobEntityId);
            if (tpl is not null) tpl.MobType = mobType;
        }

        public void SetMobProtection(uint mobEntityId, ProtectionFlags flags)
        {
            // Update the live entity: add/update or remove the ProtectionComponent.
            if (flags == ProtectionFlags.None)
            {
                _entityService.RemoveComponent<ProtectionComponent>(mobEntityId);
            }
            else
            {
                if (_entityService.TryGet<ProtectionComponent>(mobEntityId, out var existing))
                    existing.Flags = flags;
                else
                    _entityService.AddComponent(mobEntityId, new ProtectionComponent { Flags = flags });
            }

            // Update the in-memory template.
            var tpl = TryGetTemplate(mobEntityId);
            if (tpl is not null) tpl.Protection = flags;
        }

        public void SetMobTier(uint mobEntityId, int tier)
        {
            if (_entityService.TryGet<MobDataComponent>(mobEntityId, out var mob))
                mob.Tier = tier;

            var tpl = TryGetTemplate(mobEntityId);
            if (tpl is not null) tpl.Tier = tier;
        }

        public void SetMobBand(uint mobEntityId, int band)
        {
            if (_entityService.TryGet<MobDataComponent>(mobEntityId, out var mob))
                mob.Band = band;

            var tpl = TryGetTemplate(mobEntityId);
            if (tpl is not null) tpl.Band = band;
        }

        public void SetMobXpScale(uint mobEntityId, double xpScale)
        {
            if (_entityService.TryGet<MobDataComponent>(mobEntityId, out var mob))
                mob.XpScale = xpScale;

            var tpl = TryGetTemplate(mobEntityId);
            if (tpl is not null) tpl.XpScale = xpScale;
        }

        public void SetMobShop(
            uint mobEntityId,
            bool isShop,
            CurrencyId acceptedCurrency = CurrencyId.Coin,
            long tillSeed = 0,
            decimal? ratioOverride = null,
            IReadOnlyList<ShopStockRow>? baseStock = null)
        {
            if (!isShop)
            {
                // Remove shop from live entity.
                _entityService.RemoveComponent<ShopComponent>(mobEntityId);

                // Clear the template's shop fields.
                var removeTpl = TryGetTemplate(mobEntityId);
                if (removeTpl is not null)
                {
                    removeTpl.IsShop = false;
                    removeTpl.ShopAcceptedCurrency = CurrencyId.Coin;
                    removeTpl.ShopTillSeed = 0;
                    removeTpl.ShopRatioOverride = null;
                    removeTpl.ShopBaseStock.Clear();
                }
                return;
            }

            // Add or update ShopComponent on the live entity.
            if (_entityService.TryGet<ShopComponent>(mobEntityId, out var shop))
            {
                shop.AcceptedCurrency = acceptedCurrency;
                shop.TillSeed = tillSeed;
                shop.RatioOverride = ratioOverride;
                if (baseStock is not null)
                {
                    shop.BaseStock.Clear();
                    shop.BaseStock.AddRange(baseStock);
                }
            }
            else
            {
                var newShop = new ShopComponent
                {
                    AcceptedCurrency = acceptedCurrency,
                    TillSeed = tillSeed,
                    RatioOverride = ratioOverride,
                };
                if (baseStock is not null)
                    newShop.BaseStock.AddRange(baseStock);
                _entityService.AddComponent(mobEntityId, newShop);
            }

            // Ensure the shopkeeper has an inventory.
            if (!_entityService.HasComponent<InventoryComponent>(mobEntityId))
                _entityService.AddComponent(mobEntityId, new InventoryComponent());

            // Update the in-memory template.
            var tpl = TryGetTemplate(mobEntityId);
            if (tpl is not null)
            {
                tpl.IsShop = true;
                tpl.ShopAcceptedCurrency = acceptedCurrency;
                tpl.ShopTillSeed = tillSeed;
                tpl.ShopRatioOverride = ratioOverride;
                if (baseStock is not null)
                {
                    tpl.ShopBaseStock.Clear();
                    tpl.ShopBaseStock.AddRange(baseStock);
                }
            }
        }

        public void SetAttribute(uint mobEntityId, MobTemplate template, string property, int value)
        {
            switch (property)
            {
                case "level":
                    if (_entityService.TryGet<AttributesComponent>(mobEntityId, out var attrL))
                        attrL.Level = value;
                    template.Level = value;
                    break;

                case "hp":
                    if (_entityService.TryGet<PoolsComponent>(mobEntityId, out var pools))
                    {
                        pools.MaxHp = value;
                        if (pools.CurrentHp > pools.MaxHp)
                            pools.CurrentHp = pools.MaxHp;
                    }
                    template.MaxHp = value;
                    break;

                case "mind":
                    if (_entityService.TryGet<AttributesComponent>(mobEntityId, out var attrM))
                        attrM.Mind = value;
                    template.Mind = value;
                    break;

                case "body":
                    if (_entityService.TryGet<AttributesComponent>(mobEntityId, out var attrB))
                        attrB.Body = value;
                    template.Body = value;
                    break;

                case "spirit":
                    if (_entityService.TryGet<AttributesComponent>(mobEntityId, out var attrSp))
                        attrSp.Spirit = value;
                    template.Spirit = value;
                    break;

                case "attunement":
                    if (_entityService.TryGet<AttributesComponent>(mobEntityId, out var attrAt))
                        attrAt.Attunement = value;
                    template.Attunement = value;
                    break;

                case "maxmana":
                    if (_entityService.TryGet<PoolsComponent>(mobEntityId, out var poolsMana))
                    {
                        poolsMana.MaxMana = value;
                        if (poolsMana.CurrentMana > poolsMana.MaxMana)
                            poolsMana.CurrentMana = poolsMana.MaxMana;
                    }
                    template.MaxMana = value;
                    break;

                case "maxstamina":
                    if (_entityService.TryGet<PoolsComponent>(mobEntityId, out var poolsStam))
                    {
                        poolsStam.MaxStamina = value;
                        if (poolsStam.CurrentStamina > poolsStam.MaxStamina)
                            poolsStam.CurrentStamina = poolsStam.MaxStamina;
                    }
                    template.MaxStamina = value;
                    break;

                case "maxastra":
                    if (_entityService.TryGet<PoolsComponent>(mobEntityId, out var poolsAstra))
                    {
                        poolsAstra.MaxAstra = value;
                        if (poolsAstra.CurrentAstra > poolsAstra.MaxAstra)
                            poolsAstra.CurrentAstra = poolsAstra.MaxAstra;
                    }
                    template.MaxAstra = value;
                    break;
            }
        }

        private MobTemplate? TryGetTemplate(uint mobEntityId)
        {
            if (_entityService.TryGet<BlueprintComponent>(mobEntityId, out var bp) &&
                _templateRegistry.TryGet(bp.BlueprintId, out var template) &&
                template is MobTemplate mobTemplate)
                return mobTemplate;
            return null;
        }

        private string GenerateUniqueBlueprintId()
        {
            const int maxAttempts = 10;
            for (var i = 0; i < maxAttempts; i++)
            {
                var id = "mob.adhoc." + ToBase36(Guid.NewGuid())[..8];
                if (!_templateRegistry.TryGet(id, out _))
                    return id;
            }
            return "mob.adhoc." + Guid.NewGuid().ToString("N")[..16];
        }

        private static string ToBase36(Guid guid)
        {
            const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
            var bytes = guid.ToByteArray();
            var raw = BitConverter.ToInt64(bytes, 0);
            // long.MinValue overflows Math.Abs — substitute long.MaxValue (still random enough).
            var value = raw == long.MinValue ? long.MaxValue : Math.Abs(raw);
            if (value == 0) return "00000000";
            var result = new System.Text.StringBuilder();
            while (value > 0)
            {
                result.Insert(0, chars[(int)(value % 36)]);
                value /= 36;
            }
            // Pad to guarantee the caller's [..8] slice never throws.
            return result.ToString().PadLeft(8, '0');
        }
    }
}
