using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;
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
        private readonly IAttributeSystem _attributeSystem;
        private readonly ILogger<MobBuilderSystem> _logger;

        public MobBuilderSystem(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            IAttributeSystem attributeSystem,
            ILogger<MobBuilderSystem> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _attributeSystem = attributeSystem;
            _logger = logger;
        }

        public MobCreationResult CreateMob(string name)
        {
            var blueprintId = GenerateUniqueBlueprintId();
            var template = new MobTemplate(blueprintId) { Name = name };
            _templateRegistry.Register(blueprintId, template);

            _logger.LogDebug("MobBuilderSystem: registered mob template blueprint={BlueprintId}", blueprintId);
            return new MobCreationResult(blueprintId, template);
        }

        public int SetMobName(string blueprintId, string name) =>
            Apply(blueprintId, tpl => tpl.Name = name, id =>
            {
                if (_entityService.TryGet<MobDataComponent>(id, out var mob)) mob.Name = name;
            });

        public int SetMobDescription(string blueprintId, string description) =>
            Apply(blueprintId, tpl => tpl.Description = description, id =>
            {
                if (_entityService.TryGet<MobDataComponent>(id, out var mob)) mob.Description = description;
            });

        public int SetMobKeywords(string blueprintId, IReadOnlyList<string> keywords) =>
            Apply(blueprintId,
                tpl =>
                {
                    tpl.Keywords.Clear();
                    tpl.Keywords.AddRange(keywords);
                },
                id =>
                {
                    if (!_entityService.TryGet<MobDataComponent>(id, out var mob)) return;
                    mob.Keywords.Clear();
                    mob.Keywords.AddRange(keywords);
                });

        public int SetMobType(string blueprintId, MobType mobType) =>
            Apply(blueprintId, tpl => tpl.MobType = mobType, id =>
            {
                if (_entityService.TryGet<MobDataComponent>(id, out var mob)) mob.MobType = mobType;
            });

        public int SetMobProtection(string blueprintId, ProtectionFlags flags) =>
            Apply(blueprintId, tpl => tpl.Protection = flags, id =>
            {
                if (flags == ProtectionFlags.None)
                    _entityService.RemoveComponent<ProtectionComponent>(id);
                else if (_entityService.TryGet<ProtectionComponent>(id, out var existing))
                    existing.Flags = flags;
                else
                    _entityService.AddComponent(id, new ProtectionComponent { Flags = flags });
            });

        public int SetMobTier(string blueprintId, int tier) =>
            Apply(blueprintId, tpl => tpl.Tier = tier, id =>
            {
                if (_entityService.TryGet<MobDataComponent>(id, out var mob)) mob.Tier = tier;
            });

        public int SetMobBand(string blueprintId, int band) =>
            Apply(blueprintId, tpl => tpl.Band = band, id =>
            {
                if (_entityService.TryGet<MobDataComponent>(id, out var mob)) mob.Band = band;
            });

        public int SetMobXpScale(string blueprintId, double xpScale) =>
            Apply(blueprintId, tpl => tpl.XpScale = xpScale, id =>
            {
                if (_entityService.TryGet<MobDataComponent>(id, out var mob)) mob.XpScale = xpScale;
            });

        public int SetMobShop(
            string blueprintId,
            bool isShop,
            CurrencyId acceptedCurrency = CurrencyId.Coin,
            long tillSeed = 0,
            decimal? ratioOverride = null,
            IReadOnlyList<ShopStockRow>? baseStock = null)
        {
            if (!isShop)
            {
                return Apply(blueprintId,
                    tpl =>
                    {
                        tpl.IsShop = false;
                        tpl.ShopAcceptedCurrency = CurrencyId.Coin;
                        tpl.ShopTillSeed = 0;
                        tpl.ShopRatioOverride = null;
                        tpl.ShopBaseStock.Clear();
                    },
                    id => _entityService.RemoveComponent<ShopComponent>(id));
            }

            return Apply(blueprintId,
                tpl =>
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
                },
                id =>
                {
                    if (_entityService.TryGet<ShopComponent>(id, out var shop))
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
                        _entityService.AddComponent(id, newShop);
                    }

                    // Ensure the shopkeeper has an inventory.
                    if (!_entityService.HasComponent<InventoryComponent>(id))
                        _entityService.AddComponent(id, new InventoryComponent());
                });
        }

        public int SetAttribute(string blueprintId, string property, int value)
        {
            Action<MobTemplate> onTemplate;
            Action<uint> onInstance;
            switch (property)
            {
                case "level":
                    onTemplate = t => t.Level = value;
                    onInstance = id => { if (_entityService.TryGet<AttributesComponent>(id, out var a)) a.Level = value; };
                    break;
                case "mind":
                    onTemplate = t => t.Mind = value;
                    onInstance = id => { if (_entityService.TryGet<AttributesComponent>(id, out var a)) a.Mind = value; };
                    break;
                case "body":
                    onTemplate = t => t.Body = value;
                    onInstance = id => { if (_entityService.TryGet<AttributesComponent>(id, out var a)) a.Body = value; };
                    break;
                case "spirit":
                    onTemplate = t => t.Spirit = value;
                    onInstance = id => { if (_entityService.TryGet<AttributesComponent>(id, out var a)) a.Spirit = value; };
                    break;
                case "attunement":
                    onTemplate = t => t.Attunement = value;
                    onInstance = id => { if (_entityService.TryGet<AttributesComponent>(id, out var a)) a.Attunement = value; };
                    break;
                case "hp":
                    onTemplate = t => t.MaxHp = value;
                    // Base max on the live instance; current clamps to the effective cap.
                    onInstance = id => _attributeSystem.SetMax(id, ResourceType.Hp, value);
                    break;
                case "maxmana":
                    onTemplate = t => t.MaxMana = value;
                    // Base max on the live instance; current clamps to the effective cap.
                    onInstance = id => _attributeSystem.SetMax(id, ResourceType.Mana, value);
                    break;
                case "maxstamina":
                    onTemplate = t => t.MaxStamina = value;
                    // Base max on the live instance; current clamps to the effective cap.
                    onInstance = id => _attributeSystem.SetMax(id, ResourceType.Stamina, value);
                    break;
                case "maxastra":
                    onTemplate = t => t.MaxAstra = value;
                    // Base max on the live instance; current clamps to the effective cap.
                    onInstance = id => _attributeSystem.SetMax(id, ResourceType.Astra, value);
                    break;
                default:
                    return 0;
            }
            return Apply(blueprintId, onTemplate, onInstance);
        }

        /// <summary>
        /// Applies a mutation to the mob template and every live world instance of the blueprint
        /// (a <see cref="MobDataComponent"/> entity whose <see cref="BlueprintComponent"/> matches,
        /// excluding <see cref="PersistentEntity"/> — the same exclusion as the loader's
        /// live-blueprint map). Returns the instance count, or <c>-1</c> for no mob template.
        /// </summary>
        private int Apply(string blueprintId, Action<MobTemplate> onTemplate, Action<uint> onInstance)
        {
            if (!_templateRegistry.TryGet(blueprintId, out var raw) || raw is not MobTemplate template)
                return -1;

            onTemplate(template);

            var instances = new List<uint>();
            foreach (var (entityId, bp) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (!string.Equals(bp.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase)) continue;
                if (_entityService.HasComponent<PersistentEntity>(entityId)) continue;
                if (!_entityService.HasComponent<MobDataComponent>(entityId)) continue;
                instances.Add(entityId);
            }
            foreach (var entityId in instances)
                onInstance(entityId);
            return instances.Count;
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
