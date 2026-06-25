using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.World;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.Items.Systems
{
    /// <summary>
    /// YAML-based <see cref="IItemContentWriter"/>. Serializes an <see cref="ItemTemplate"/>
    /// to <c>{contentDirectory}/items/{blueprintId}.yaml</c> using an atomic write (tmp → rename).
    /// Mirrors the DTO shape used by <see cref="ItemTemplateDeserializer"/> so round-trips are
    /// lossless.
    /// </summary>
    public sealed class ItemContentWriter : IItemContentWriter
    {
        private readonly string _itemsDirectory;
        private readonly ISerializer _yaml;

        public ItemContentWriter(IOptions<WorldOptions> options)
        {
            _itemsDirectory = Path.Combine(options.Value.ContentDirectory, "items");
            _yaml = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        public async Task WriteAsync(ItemTemplate template, CancellationToken ct = default)
        {
            Directory.CreateDirectory(_itemsDirectory);

            var wornSlots = template.WornSlots.Count > 0
                ? template.WornSlots.ConvertAll(s => s.ToString().ToLowerInvariant())
                : null;

            var statBonuses = template.StatBonuses.Count > 0
                ? template.StatBonuses.ConvertAll(b => new StatBonusDto
                {
                    TargetScore = b.TargetScore.ToString().ToLowerInvariant(),
                    Magnitude = b.Magnitude,
                })
                : null;

            var dto = new ItemDto
            {
                BlueprintId = template.BlueprintId,
                Name = template.Name,
                Description = template.Description,
                Keywords = new List<string>(template.Keywords),
                ItemType = template.ItemType.ToString(),
                WornSlots = wornSlots,
                SpawnRoomId = template.SpawnRoomBlueprintId,
                StatBonuses = statBonuses,
                Value = template.Value,
            };

            var body = _yaml.Serialize(dto);
            var filePath = Path.Combine(_itemsDirectory, $"{template.BlueprintId}.yaml");
            var tmpPath = filePath + ".tmp";

            await File.WriteAllTextAsync(tmpPath, body, ct).ConfigureAwait(false);
            File.Move(tmpPath, filePath, overwrite: true);
        }

        private sealed class ItemDto
        {
            public string BlueprintId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<string> Keywords { get; set; } = new();
            public string ItemType { get; set; } = string.Empty;
            public List<string>? WornSlots { get; set; }
            public string SpawnRoomId { get; set; } = string.Empty;
            public List<StatBonusDto>? StatBonuses { get; set; }
            public long Value { get; set; } = 0;
        }

        private sealed class StatBonusDto
        {
            public string TargetScore { get; set; } = string.Empty;
            public int Magnitude { get; set; }
        }
    }
}
