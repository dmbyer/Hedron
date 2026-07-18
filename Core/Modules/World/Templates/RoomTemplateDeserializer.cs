using System;
using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.World.Templates
{
    /// <summary>
    /// YAML → <see cref="RoomTemplate"/> translator for the World module. Registered as an
    /// <see cref="ITemplateDeserializer"/> with kind <c>"room"</c>; the cross-cutting
    /// <see cref="YamlContentSerializer"/> dispatches to it by kind.
    /// </summary>
    public sealed class RoomTemplateDeserializer : ITemplateDeserializer
    {
        private const int CurrentSchemaVersion = 1;

        private readonly ILogger<RoomTemplateDeserializer> _logger;
        private readonly IDeserializer _yaml;

        public string Kind => "room";

        public RoomTemplateDeserializer(ILogger<RoomTemplateDeserializer> logger)
        {
            _logger = logger;
            _yaml = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }

        public IEntityTemplate Deserialize(string fileBody)
        {
            var dto = _yaml.Deserialize<RoomDto>(fileBody)
                ?? throw new InvalidOperationException("Empty room YAML.");

            if (dto.SchemaVersion is { } v && v != CurrentSchemaVersion)
                _logger.LogWarning(
                    "Room '{Id}' declares schemaVersion {Declared}; current is {Current}. Loading anyway.",
                    dto.Id, v, CurrentSchemaVersion);

            if (string.IsNullOrWhiteSpace(dto.Id))
                throw new InvalidOperationException("Room file is missing required 'id' field.");

            var template = new RoomTemplate(dto.Id)
            {
                Name = dto.Name ?? string.Empty,
                Description = dto.Description ?? string.Empty,
                AreaId = dto.AreaId ?? string.Empty,
                SchemaVersion = dto.SchemaVersion,
                X = dto.X,
                Y = dto.Y,
                Z = dto.Z,
            };

            if (dto.Exits is { Count: > 0 })
            {
                foreach (var (rawDirection, targetBlueprintId) in dto.Exits)
                {
                    if (!Enum.TryParse<Direction>(rawDirection, ignoreCase: true, out var direction))
                    {
                        _logger.LogWarning(
                            "Room '{BlueprintId}': unknown exit direction '{Direction}' — skipping.",
                            dto.Id, rawDirection);
                        continue;
                    }
                    template.Exits[direction] = targetBlueprintId;
                }
            }

            if (dto.SpawnRules is { Count: > 0 })
            {
                foreach (var ruleDto in dto.SpawnRules)
                {
                    if (string.IsNullOrWhiteSpace(ruleDto.BlueprintId))
                    {
                        _logger.LogWarning(
                            "Room '{BlueprintId}': spawn rule missing blueprintId — skipping.",
                            dto.Id);
                        continue;
                    }
                    template.SpawnRules.Add(new SpawnRule(
                        ruleDto.BlueprintId,
                        ruleDto.MinCount ?? 1,
                        ruleDto.MaxCount ?? 1,
                        ruleDto.RespawnDelaySeconds ?? 300));
                }
            }

            return template;
        }

        private sealed class RoomDto
        {
            public int? SchemaVersion { get; set; }
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? AreaId { get; set; }
            public int? X { get; set; }
            public int? Y { get; set; }
            public int? Z { get; set; }
            public Dictionary<string, string>? Exits { get; set; }
            public List<SpawnRuleDto>? SpawnRules { get; set; }
        }

        private sealed class SpawnRuleDto
        {
            public string? BlueprintId { get; set; }
            public int? MinCount { get; set; }
            public int? MaxCount { get; set; }
            public int? RespawnDelaySeconds { get; set; }
        }
    }
}
