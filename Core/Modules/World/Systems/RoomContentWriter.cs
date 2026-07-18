using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.World.Templates;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// YAML-based <see cref="IRoomContentWriter"/>. Serializes a <see cref="RoomTemplate"/>
    /// to <c>{contentDirectory}/rooms/{blueprintId}.yaml</c> using an atomic write (tmp → rename).
    /// Mirrors the DTO shape used by <see cref="RoomTemplateDeserializer"/> so round-trips are
    /// lossless.
    /// </summary>
    public sealed class RoomContentWriter : IRoomContentWriter
    {
        private readonly string _roomsDirectory;
        private readonly ISerializer _yaml;

        public RoomContentWriter(IOptions<WorldOptions> options)
        {
            _roomsDirectory = System.IO.Path.Combine(options.Value.ContentDirectory, "rooms");
            _yaml = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        public async Task WriteAsync(RoomTemplate template, CancellationToken ct = default)
        {
            Directory.CreateDirectory(_roomsDirectory);

            // Convert Direction enum keys → string keys to match the deserializer's DTO shape.
            var exitDtos = new Dictionary<string, string>();
            foreach (var (direction, targetBlueprintId) in template.Exits)
                exitDtos[direction.ToString()] = targetBlueprintId;

            List<SpawnRuleDto>? spawnRuleDtos = null;
            if (template.SpawnRules.Count > 0)
            {
                spawnRuleDtos = new List<SpawnRuleDto>(template.SpawnRules.Count);
                foreach (var rule in template.SpawnRules)
                {
                    spawnRuleDtos.Add(new SpawnRuleDto
                    {
                        BlueprintId = rule.BlueprintId,
                        MinCount = rule.MinCount,
                        MaxCount = rule.MaxCount,
                        RespawnDelaySeconds = rule.RespawnDelaySeconds,
                    });
                }
            }

            var dto = new RoomDto
            {
                SchemaVersion = template.SchemaVersion,
                Id          = template.BlueprintId,
                Name        = template.Name,
                Description = template.Description,
                AreaId      = string.IsNullOrEmpty(template.AreaId) ? null : template.AreaId,
                X           = template.X,
                Y           = template.Y,
                Z           = template.Z,
                Exits       = exitDtos.Count > 0 ? exitDtos : null,
                SpawnRules  = spawnRuleDtos,
            };

            var body     = _yaml.Serialize(dto);
            var filePath = Path.Combine(_roomsDirectory, $"{template.BlueprintId}.yaml");
            var tmpPath  = filePath + ".tmp";

            await File.WriteAllTextAsync(tmpPath, body, ct).ConfigureAwait(false);
            File.Move(tmpPath, filePath, overwrite: true);
        }

        // DTO shape must stay in sync with RoomTemplateDeserializer.RoomDto.
        private sealed class RoomDto
        {
            public int? SchemaVersion { get; set; }
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string? AreaId { get; set; }
            public int? X { get; set; }
            public int? Y { get; set; }
            public int? Z { get; set; }
            public Dictionary<string, string>? Exits { get; set; }
            public List<SpawnRuleDto>? SpawnRules { get; set; }
        }

        // DTO shape must stay in sync with RoomTemplateDeserializer.SpawnRuleDto.
        private sealed class SpawnRuleDto
        {
            public string BlueprintId { get; set; } = string.Empty;
            public int MinCount { get; set; }
            public int MaxCount { get; set; }
            public int RespawnDelaySeconds { get; set; }
        }
    }
}
