using System;
using System.Collections.Generic;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Items.Systems
{
    public sealed class ItemBuilderSystem : IItemBuilderSystem
    {
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly ILogger<ItemBuilderSystem> _logger;

        public ItemBuilderSystem(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            ILogger<ItemBuilderSystem> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _logger = logger;
        }

        public ItemCreationResult CreateItem(string name, uint roomEntityId)
        {
            var blueprintId = GenerateUniqueBlueprintId();

            var spawnRoomBlueprintId = string.Empty;
            if (_entityService.TryGet<BlueprintComponent>(roomEntityId, out var roomBp))
                spawnRoomBlueprintId = roomBp.BlueprintId;

            var entity = _entityService.CreateEntity();
            _entityService.AddComponent(entity.Id, new ItemDataComponent { Name = name });
            _entityService.AddComponent(entity.Id, new BlueprintComponent { BlueprintId = blueprintId });
            _entityService.AddComponent(entity.Id, new LocationComponent
            {
                RoomEntityId = roomEntityId,
                RoomBlueprintId = spawnRoomBlueprintId,
            });

            var template = new ItemTemplate(blueprintId)
            {
                Name = name,
                SpawnRoomBlueprintId = spawnRoomBlueprintId,
            };
            _templateRegistry.Register(blueprintId, template);

            _logger.LogDebug(
                "ItemBuilderSystem: created item entity={EntityId} blueprint={BlueprintId} spawnRoom={SpawnRoom}",
                entity.Id, blueprintId, spawnRoomBlueprintId);

            return new ItemCreationResult(entity.Id, blueprintId, template);
        }

        public void SetItemName(uint itemEntityId, string name)
        {
            if (_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
                item.Name = name;
            var tpl = TryGetTemplate(itemEntityId);
            if (tpl is not null) tpl.Name = name;
        }

        public void SetItemDescription(uint itemEntityId, string description)
        {
            if (_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
                item.Description = description;
            var tpl = TryGetTemplate(itemEntityId);
            if (tpl is not null) tpl.Description = description;
        }

        public void SetItemKeywords(uint itemEntityId, IReadOnlyList<string> keywords)
        {
            if (_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
            {
                item.Keywords.Clear();
                item.Keywords.AddRange(keywords);
            }
            var tpl = TryGetTemplate(itemEntityId);
            if (tpl is not null)
            {
                tpl.Keywords.Clear();
                tpl.Keywords.AddRange(keywords);
            }
        }

        public void SetItemType(uint itemEntityId, ItemType itemType)
        {
            if (_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
                item.ItemType = itemType;
            var tpl = TryGetTemplate(itemEntityId);
            if (tpl is not null) tpl.ItemType = itemType;
        }

        public void SetItemSlots(uint itemEntityId, IReadOnlyList<WornSlot> slots)
        {
            if (_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
            {
                if (slots.Count == 0)
                    item.WornSlots = null;
                else
                {
                    item.WornSlots ??= new List<WornSlot>();
                    item.WornSlots.Clear();
                    item.WornSlots.AddRange(slots);
                }
            }
            var tpl = TryGetTemplate(itemEntityId);
            if (tpl is not null)
            {
                tpl.WornSlots.Clear();
                tpl.WornSlots.AddRange(slots);
            }
        }

        public void SetItemDamageBonus(uint itemEntityId, int value)
        {
            if (_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
                item.DamageBonus = value;
            var tpl = TryGetTemplate(itemEntityId);
            if (tpl is not null) tpl.DamageBonus = value;
        }

        private ItemTemplate? TryGetTemplate(uint itemEntityId)
        {
            if (_entityService.TryGet<BlueprintComponent>(itemEntityId, out var bp) &&
                _templateRegistry.TryGet(bp.BlueprintId, out var template) &&
                template is ItemTemplate itemTemplate)
                return itemTemplate;
            return null;
        }

        private string GenerateUniqueBlueprintId()
        {
            const int maxAttempts = 10;
            for (var i = 0; i < maxAttempts; i++)
            {
                var id = "item.adhoc." + ToBase36(Guid.NewGuid())[..8];
                if (!_templateRegistry.TryGet(id, out _))
                    return id;
            }
            return "item.adhoc." + Guid.NewGuid().ToString("N")[..16];
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
