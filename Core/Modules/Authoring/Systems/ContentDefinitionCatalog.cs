using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// Default <see cref="IContentDefinitionCatalog"/>. Composes the existing per-kind content
    /// writers, the content serializer (read side), and the content validator into one
    /// surface-agnostic authoring facade.
    /// </summary>
    public sealed class ContentDefinitionCatalog : IContentDefinitionCatalog
    {
        private readonly IContentSerializer _serializer;
        private readonly IContentValidator _validator;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IAreaContentWriter _areaWriter;
        private readonly IRoomContentWriter _roomWriter;
        private readonly IItemContentWriter _itemWriter;
        private readonly IMobContentWriter _mobWriter;
        private readonly ILogger<ContentDefinitionCatalog> _logger;
        private readonly string _contentDirectory;

        public ContentDefinitionCatalog(
            IContentSerializer serializer,
            IContentValidator validator,
            ITemplateRegistry templateRegistry,
            IAreaContentWriter areaWriter,
            IRoomContentWriter roomWriter,
            IItemContentWriter itemWriter,
            IMobContentWriter mobWriter,
            IOptions<WorldOptions> options,
            ILogger<ContentDefinitionCatalog> logger)
        {
            _serializer = serializer;
            _validator = validator;
            _templateRegistry = templateRegistry;
            _areaWriter = areaWriter;
            _roomWriter = roomWriter;
            _itemWriter = itemWriter;
            _mobWriter = mobWriter;
            _logger = logger;
            _contentDirectory = options.Value.ContentDirectory;
        }

        public IReadOnlyList<ContentSummary> List(ContentKind kind)
        {
            var directory = Path.Combine(_contentDirectory, kind.Subdirectory());
            if (!Directory.Exists(directory))
                return Array.Empty<ContentSummary>();

            var summaries = new List<ContentSummary>();
            foreach (var file in Directory.GetFiles(directory, "*" + _serializer.FormatExtension))
            {
                try
                {
                    var body = File.ReadAllText(file);
                    var template = _serializer.Deserialize(kind.KindString(), body);
                    var (name, description) = ExtractNameAndDescription(template);
                    summaries.Add(new ContentSummary(template.BlueprintId, name, description));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "ContentDefinitionCatalog: failed to read {Kind} file '{File}'; skipping in listing.",
                        kind, file);
                }
            }
            return summaries;
        }

        public ContentDefinition? Load(ContentKind kind, string blueprintId)
        {
            var path = Path.Combine(_contentDirectory, kind.Subdirectory(), blueprintId + _serializer.FormatExtension);
            if (!File.Exists(path))
                return null;

            var body = File.ReadAllText(path);
            var template = _serializer.Deserialize(kind.KindString(), body);
            return new ContentDefinition(kind, template);
        }

        public async Task<ContentWriteResult> SaveAsync(ContentDefinition definition, CancellationToken ct = default)
        {
            var report = _validator.Validate(definition.Template);
            if (!report.IsValid)
                return ContentWriteResult.Failed(definition.BlueprintId, report.Errors);

            switch (definition.Kind)
            {
                case ContentKind.Area:
                    await _areaWriter.WriteAsync((AreaTemplate)definition.Template, ct).ConfigureAwait(false);
                    break;
                case ContentKind.Room:
                    await _roomWriter.WriteAsync((RoomTemplate)definition.Template, ct).ConfigureAwait(false);
                    break;
                case ContentKind.Item:
                    await _itemWriter.WriteAsync((ItemTemplate)definition.Template, ct).ConfigureAwait(false);
                    break;
                case ContentKind.Mob:
                    await _mobWriter.WriteAsync((MobTemplate)definition.Template, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(definition));
            }

            return ContentWriteResult.Ok(definition.BlueprintId);
        }

        public ContentDefinition CreateNew(ContentKind kind, string name)
        {
            var blueprintId = AdhocBlueprintId.Generate(
                kind.AdhocPrefix(),
                id => _templateRegistry.TryGet(id, out _));

            IEntityTemplate template = kind switch
            {
                ContentKind.Area => new AreaTemplate(blueprintId) { Name = name },
                ContentKind.Room => new RoomTemplate(blueprintId) { Name = name },
                ContentKind.Item => new ItemTemplate(blueprintId) { Name = name },
                ContentKind.Mob  => new MobTemplate(blueprintId)  { Name = name },
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

            return new ContentDefinition(kind, template);
        }

        private static (string Name, string Description) ExtractNameAndDescription(IEntityTemplate template) => template switch
        {
            AreaTemplate a => (a.Name, a.Description),
            RoomTemplate r => (r.Name, r.Description),
            ItemTemplate i => (i.Name, i.Description),
            MobTemplate m  => (m.Name, m.Description),
            _ => (template.BlueprintId, string.Empty),
        };
    }
}
