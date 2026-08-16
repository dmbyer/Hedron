using System;
using System.Collections.Generic;
using System.Threading;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.Items
{
    /// <summary>
    /// YAML → <see cref="ItemTemplate"/> translator for the Items module. Registered as an
    /// <see cref="ITemplateDeserializer"/> with kind <c>"item"</c>.
    /// </summary>
    public sealed class ItemTemplateDeserializer : ITemplateDeserializer
    {
        private readonly ILogger<ItemTemplateDeserializer> _logger;
        // YamlDotNet's IDeserializer is NOT thread-safe: its type inspector caches into plain
        // dictionaries, so two threads deserializing at once can corrupt that cache. The content
        // catalog serves reads lock-free and concurrently — Blazor circuits and, since the authoring
        // JSON surface landed, request threads — so a single shared instance is unsafe here (INV-31).
        // One instance per thread keeps reads parallel; rebuilding per call would re-pay the builder
        // cost on every file of a corpus sweep. Never disposed: this is a process-lifetime singleton.
        private readonly ThreadLocal<IDeserializer> _yaml = new(() => new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build());

        public string Kind => "item";

        public ItemTemplateDeserializer(ILogger<ItemTemplateDeserializer> logger)
        {
            _logger = logger;
        }

        public IEntityTemplate Deserialize(string fileBody)
        {
            var dto = _yaml.Value!.Deserialize<ItemDto>(fileBody)
                ?? throw new InvalidOperationException("Empty item YAML.");

            if (string.IsNullOrWhiteSpace(dto.BlueprintId))
                throw new InvalidOperationException("Item file is missing required 'blueprintId' field.");

            var template = new ItemTemplate(dto.BlueprintId)
            {
                Name = dto.Name ?? string.Empty,
                Description = dto.Description ?? string.Empty,
                SpawnRoomBlueprintId = dto.SpawnRoomId ?? string.Empty,
                Value = dto.Value,
            };

            if (dto.Keywords is { Count: > 0 })
                template.Keywords.AddRange(dto.Keywords);

            if (!string.IsNullOrEmpty(dto.ItemType) &&
                Enum.TryParse<ItemType>(dto.ItemType, ignoreCase: true, out var itemType))
                template.ItemType = itemType;
            else if (!string.IsNullOrEmpty(dto.ItemType))
                _logger.LogWarning(
                    "Item '{Id}': unknown itemType '{Type}' — defaulting to None.",
                    dto.BlueprintId, dto.ItemType);

            if (dto.WornSlots is { Count: > 0 })
            {
                foreach (var slotStr in dto.WornSlots)
                {
                    if (Enum.TryParse<WornSlot>(slotStr, ignoreCase: true, out var slot))
                        template.WornSlots.Add(slot);
                    else
                        _logger.LogWarning(
                            "Item '{Id}': unknown wornSlot '{Slot}' — skipping.",
                            dto.BlueprintId, slotStr);
                }
            }

            if (dto.StatBonuses is { Count: > 0 })
            {
                foreach (var bonus in dto.StatBonuses)
                {
                    if (!string.IsNullOrWhiteSpace(bonus.TargetScore) &&
                        Enum.TryParse<ScoreId>(bonus.TargetScore, ignoreCase: true, out var score))
                        template.StatBonuses.Add(new EquipmentStatBonus(score, bonus.Magnitude));
                    else
                        _logger.LogWarning(
                            "Item '{Id}': unknown statBonus score '{Score}' — skipping.",
                            dto.BlueprintId, bonus.TargetScore);
                }
            }

            // Deserialize the Tier tag. Null/absent → 0 (unbanded/base, the default).
            if (dto.Tier.HasValue)
            {
                if (dto.Tier.Value is >= 0 and <= 6)
                {
                    template.Tier = dto.Tier.Value;
                }
                else
                {
                    _logger.LogWarning(
                        "Item '{Id}': tier '{Tier}' out of range 0-6 — defaulting to 0 (unbanded).",
                        dto.BlueprintId, dto.Tier.Value);
                }
            }

            // Deserialize the Band tag. Null/absent → 0 (unbanded, the default). Clean-break note:
            // this key reused the old single-axis "band:" name, so a legacy value in [1,3] is
            // silently reinterpreted as new-axis band N (tier stays 0/untagged); a legacy value in
            // [4,6] fails this range check and warns-and-untags — both acceptable under the
            // accepted thin-content clean break (see power-model-revision.md Design notes).
            if (dto.Band.HasValue)
            {
                if (dto.Band.Value is >= 0 and <= 3)
                {
                    template.Band = dto.Band.Value;
                }
                else
                {
                    _logger.LogWarning(
                        "Item '{Id}': band '{Band}' out of range 0-3 — defaulting to 0 (unbanded).",
                        dto.BlueprintId, dto.Band.Value);
                }
            }

            return template;
        }

        private sealed class ItemDto
        {
            public string? BlueprintId { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public List<string>? Keywords { get; set; }
            public string? ItemType { get; set; }
            public List<string>? WornSlots { get; set; }
            public string? SpawnRoomId { get; set; }
            public List<StatBonusDto>? StatBonuses { get; set; }
            public long Value { get; set; } = 0;
            public int? Tier { get; set; }
            public int? Band { get; set; }
        }

        private sealed class StatBonusDto
        {
            public string? TargetScore { get; set; }
            public int Magnitude { get; set; }
        }
    }
}
