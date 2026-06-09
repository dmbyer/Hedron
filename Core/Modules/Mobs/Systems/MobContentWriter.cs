using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.Mobs.Systems
{
    public sealed class MobContentWriter : IMobContentWriter
    {
        private readonly string _mobsDirectory;
        private readonly ISerializer _yaml;

        public MobContentWriter(IOptions<WorldOptions> options)
        {
            _mobsDirectory = Path.Combine(options.Value.ContentDirectory, "mobs");
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
                Level = template.Level,
                MaxHp = template.MaxHp,
                Mind = template.Mind,
                Body = template.Body,
                Spirit = template.Spirit,
                Attunement = template.Attunement,
                MaxMana = template.MaxMana,
                MaxStamina = template.MaxStamina,
                MaxAstra = template.MaxAstra,
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
            public int Level { get; set; }
            public int MaxHp { get; set; }
            public int Mind { get; set; }
            public int Body { get; set; }
            public int Spirit { get; set; }
            public int Attunement { get; set; }
            public int MaxMana { get; set; }
            public int MaxStamina { get; set; }
            public int MaxAstra { get; set; }
        }
    }
}
