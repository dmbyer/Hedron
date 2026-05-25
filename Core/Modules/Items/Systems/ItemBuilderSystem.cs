using System;
using System.Collections.Generic;
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

            var entity = _entityService.CreateEntity();
            _entityService.AddComponent(entity.Id, new ItemDataComponent { Name = name });
            _entityService.AddComponent(entity.Id, new BlueprintComponent { BlueprintId = blueprintId });
            _entityService.AddComponent(entity.Id, new PersistentEntity());
            _entityService.AddComponent(entity.Id, new LocationComponent { RoomEntityId = roomEntityId });

            var template = new ItemTemplate(blueprintId) { Name = name };
            _templateRegistry.Register(blueprintId, template);

            _logger.LogDebug(
                "ItemBuilderSystem: created item entity={EntityId} blueprint={BlueprintId}",
                entity.Id, blueprintId);

            return new ItemCreationResult(entity.Id, blueprintId);
        }

        public void SetItemName(uint itemEntityId, string name)
        {
            if (_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
                item.Name = name;
        }

        public void SetItemDescription(uint itemEntityId, string description)
        {
            if (_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
                item.Description = description;
        }

        public void SetItemKeywords(uint itemEntityId, IReadOnlyList<string> keywords)
        {
            if (_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
            {
                item.Keywords.Clear();
                item.Keywords.AddRange(keywords);
            }
        }

        public void SetItemType(uint itemEntityId, ItemType itemType)
        {
            if (_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
                item.ItemType = itemType;
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
            var value = Math.Abs(BitConverter.ToInt64(bytes, 0));
            if (value == 0) return "0";
            var result = new System.Text.StringBuilder();
            while (value > 0)
            {
                result.Insert(0, chars[(int)(value % 36)]);
                value /= 36;
            }
            return result.ToString();
        }
    }
}
