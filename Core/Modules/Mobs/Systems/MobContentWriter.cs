using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Mobs.Templates;
using Microsoft.Extensions.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.Mobs.Systems
{
    public sealed class MobContentWriter : IMobContentWriter
    {
        private readonly string _mobsDirectory;
        private readonly ISerializer _yaml;

        public MobContentWriter(IConfiguration configuration)
        {
            var contentDirectory = configuration["World:ContentDirectory"] ?? "data/content/";
            _mobsDirectory = Path.Combine(contentDirectory, "mobs");
            _yaml = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        public async Task WriteAsync(MobTemplate template, CancellationToken ct = default)
        {
            Directory.CreateDirectory(_mobsDirectory);

            var dto = new MobDto
            {
                BlueprintId = template.BlueprintId,
                Name = template.Name,
                Description = template.Description,
                Keywords = new List<string>(template.Keywords),
                Type = template.MobType.ToString(),
                SpawnRoomBlueprintId = template.SpawnRoomBlueprintId,
            };

            var body = _yaml.Serialize(dto);
            var filePath = Path.Combine(_mobsDirectory, $"{template.BlueprintId}.yaml");
            var tmpPath = filePath + ".tmp";

            await File.WriteAllTextAsync(tmpPath, body, ct).ConfigureAwait(false);
            File.Move(tmpPath, filePath, overwrite: true);
        }

        private sealed class MobDto
        {
            public string BlueprintId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<string> Keywords { get; set; } = new();
            public string Type { get; set; } = string.Empty;
            public string SpawnRoomBlueprintId { get; set; } = string.Empty;
        }
    }
}
