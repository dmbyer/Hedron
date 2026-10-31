using System;
using System.Collections.Generic;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Stats;
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

        public ItemCreationResult CreateItem(string name)
        {
            var blueprintId = GenerateUniqueBlueprintId();
            var template = new ItemTemplate(blueprintId) { Name = name };
            _templateRegistry.Register(blueprintId, template);

            _logger.LogDebug("ItemBuilderSystem: registered item template blueprint={BlueprintId}", blueprintId);
            return new ItemCreationResult(blueprintId, template);
        }

        public int SetItemName(string blueprintId, string name) =>
            Apply(blueprintId, tpl => tpl.Name = name, item => item.Name = name);

        public int SetItemDescription(string blueprintId, string description) =>
            Apply(blueprintId, tpl => tpl.Description = description, item => item.Description = description);

        public int SetItemKeywords(string blueprintId, IReadOnlyList<string> keywords) =>
            Apply(blueprintId,
                tpl =>
                {
                    tpl.Keywords.Clear();
                    tpl.Keywords.AddRange(keywords);
                },
                item =>
                {
                    item.Keywords.Clear();
                    item.Keywords.AddRange(keywords);
                });

        public int SetItemType(string blueprintId, ItemType itemType) =>
            Apply(blueprintId, tpl => tpl.ItemType = itemType, item => item.ItemType = itemType);

        public int SetItemSlots(string blueprintId, IReadOnlyList<WornSlot> slots) =>
            Apply(blueprintId,
                tpl =>
                {
                    tpl.WornSlots.Clear();
                    tpl.WornSlots.AddRange(slots);
                },
                item =>
                {
                    if (slots.Count == 0)
                        item.WornSlots = null;
                    else
                    {
                        item.WornSlots ??= new List<WornSlot>();
                        item.WornSlots.Clear();
                        item.WornSlots.AddRange(slots);
                    }
                });

        public int SetItemStatBonus(string blueprintId, ScoreId score, int magnitude) =>
            Apply(blueprintId,
                tpl => ApplyStatBonus(tpl.StatBonuses, score, magnitude),
                item => ApplyStatBonus(item.StatBonuses, score, magnitude));

        public int ClearItemStatBonuses(string blueprintId) =>
            Apply(blueprintId, tpl => tpl.StatBonuses.Clear(), item => item.StatBonuses.Clear());

        public int SetItemValue(string blueprintId, long value) =>
            Apply(blueprintId, tpl => tpl.Value = value, item => item.Value = value);

        public int SetItemTier(string blueprintId, int tier) =>
            Apply(blueprintId, tpl => tpl.Tier = tier, item => item.Tier = tier);

        public int SetItemBand(string blueprintId, int band) =>
            Apply(blueprintId, tpl => tpl.Band = band, item => item.Band = band);

        /// <summary>
        /// Applies a mutation to the item template and every live world instance of the blueprint
        /// (an <see cref="ItemDataComponent"/> entity whose <see cref="BlueprintComponent"/> matches,
        /// excluding <see cref="PersistentEntity"/> — player-owned copies keep their own state, INV-21).
        /// Returns the instance count, or <c>-1</c> for no item template.
        /// </summary>
        private int Apply(string blueprintId, Action<ItemTemplate> onTemplate, Action<ItemDataComponent> onInstance)
        {
            if (!_templateRegistry.TryGet(blueprintId, out var raw) || raw is not ItemTemplate template)
                return -1;

            onTemplate(template);

            var instances = new List<ItemDataComponent>();
            foreach (var (entityId, bp) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (!string.Equals(bp.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase)) continue;
                if (_entityService.HasComponent<PersistentEntity>(entityId)) continue;
                if (!_entityService.TryGet<ItemDataComponent>(entityId, out var item)) continue;
                instances.Add(item);
            }
            foreach (var item in instances)
                onInstance(item);
            return instances.Count;
        }

        // Keep one row per ScoreId: drop any existing row for this score, then add the new one
        // unless magnitude is 0 (which means "remove the bonus").
        private static void ApplyStatBonus(List<EquipmentStatBonus> bonuses, ScoreId score, int magnitude)
        {
            bonuses.RemoveAll(b => b.TargetScore == score);
            if (magnitude != 0)
                bonuses.Add(new EquipmentStatBonus(score, magnitude));
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
