using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Items.Templates;
using Microsoft.Extensions.Configuration;
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

        public ItemContentWriter(IConfiguration configuration)
        {
            var contentDirectory = configuration["World:ContentDirectory"] ?? "data/content/";
            _itemsDirectory = Path.Combine(contentDirectory, "items");
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

            var dto = new ItemDto
            {
                BlueprintId = template.BlueprintId,
                Name = template.Name,
                Description = template.Description,
                Keywords = new List<string>(template.Keywords),
                ItemType = template.ItemType.ToString(),
                WornSlots = wornSlots,
                SpawnRoomId = template.SpawnRoomBlueprintId,
                DamageBonus = template.DamageBonus,
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
            public int DamageBonus { get; set; }
        }
    }
}
