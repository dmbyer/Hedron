using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.World.Templates;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// YAML-based <see cref="IAreaContentWriter"/>. Serializes an <see cref="AreaTemplate"/>
    /// to <c>{contentDirectory}/areas/{blueprintId}.yaml</c> using an atomic write (tmp → rename).
    /// Mirrors the DTO shape used by <see cref="AreaTemplateDeserializer"/> so round-trips are
    /// lossless.
    /// </summary>
    public sealed class AreaContentWriter : IAreaContentWriter
    {
        private readonly string _areasDirectory;
        private readonly ISerializer _yaml;

        public AreaContentWriter(IOptions<WorldOptions> options)
        {
            _areasDirectory = System.IO.Path.Combine(options.Value.ContentDirectory, "areas");
            _yaml = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        public async Task WriteAsync(AreaTemplate template, CancellationToken ct = default)
        {
            Directory.CreateDirectory(_areasDirectory);

            // Convert AspectId enum keys → string keys to match the deserializer's DTO shape.
            Dictionary<string, int>? aspectDtos = null;
            if (template.AspectAffinities is { Count: > 0 })
            {
                aspectDtos = new Dictionary<string, int>();
                foreach (var (aspect, weight) in template.AspectAffinities)
                    aspectDtos[aspect.ToString()] = weight;
            }

            var dto = new AreaDto
            {
                Id                = template.BlueprintId,
                Name              = template.Name,
                Description       = template.Description,
                RespawnRate       = template.RespawnRate,
                Pvp               = template.Pvp,
                Rooms             = template.Rooms.Count > 0 ? template.Rooms : null,
                AspectAffinities  = aspectDtos,
            };

            var body     = _yaml.Serialize(dto);
            var filePath = Path.Combine(_areasDirectory, $"{template.BlueprintId}.yaml");
            var tmpPath  = filePath + ".tmp";

            await File.WriteAllTextAsync(tmpPath, body, ct).ConfigureAwait(false);
            File.Move(tmpPath, filePath, overwrite: true);
        }

        // DTO shape must stay in sync with AreaTemplateDeserializer.AreaDto.
        private sealed class AreaDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int RespawnRate { get; set; }
            public bool Pvp { get; set; }
            public List<string>? Rooms { get; set; }
            public Dictionary<string, int>? AspectAffinities { get; set; }
        }
    }
}
