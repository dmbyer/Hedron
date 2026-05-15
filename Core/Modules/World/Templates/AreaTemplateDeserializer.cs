using System;
using System.Collections.Generic;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.World.Templates
{
    /// <summary>
    /// YAML → <see cref="AreaTemplate"/> translator for the World module. Registered as an
    /// <see cref="ITemplateDeserializer"/> with kind <c>"area"</c>.
    /// </summary>
    public sealed class AreaTemplateDeserializer : ITemplateDeserializer
    {
        private const int CurrentSchemaVersion = 1;

        private readonly ILogger<AreaTemplateDeserializer> _logger;
        private readonly IDeserializer _yaml;

        public string Kind => "area";

        public AreaTemplateDeserializer(ILogger<AreaTemplateDeserializer> logger)
        {
            _logger = logger;
            _yaml = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }

        public IEntityTemplate Deserialize(string fileBody)
        {
            var dto = _yaml.Deserialize<AreaDto>(fileBody)
                ?? throw new InvalidOperationException("Empty area YAML.");

            if (dto.SchemaVersion is { } v && v != CurrentSchemaVersion)
                _logger.LogWarning(
                    "Area '{Id}' declares schemaVersion {Declared}; current is {Current}. Loading anyway.",
                    dto.Id, v, CurrentSchemaVersion);

            if (string.IsNullOrWhiteSpace(dto.Id))
                throw new InvalidOperationException("Area file is missing required 'id' field.");

            var template = new AreaTemplate(dto.Id)
            {
                AreaId = dto.Id,
                Name = dto.Name ?? string.Empty,
                Description = dto.Description ?? string.Empty,
                RespawnRate = dto.RespawnRate,
                Pvp = dto.Pvp,
            };

            if (dto.Rooms is { Count: > 0 })
                template.Rooms.AddRange(dto.Rooms);

            return template;
        }

        private sealed class AreaDto
        {
            public int? SchemaVersion { get; set; }
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public int RespawnRate { get; set; }
            public bool Pvp { get; set; }
            public List<string>? Rooms { get; set; }
        }
    }
}
