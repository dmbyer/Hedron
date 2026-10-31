using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hedron.Core.Systems.Definitions;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// Default <see cref="IHelpTopicLayerReader"/>: the engine root is
    /// <see cref="HelpOptions.EngineTopicDirectory"/>, the world root is
    /// <see cref="HelpOptions.WorldTopicDirectory"/> (<c>&lt;World:ContentDirectory&gt;/help/</c>).
    /// Stateless and safe from any thread (INV-31).
    /// </summary>
    public sealed class HelpTopicLayerReader : IHelpTopicLayerReader
    {
        public const string WorldSubdirectory = "help";

        private readonly string _engineRoot;

        public HelpTopicLayerReader(IOptions<HelpOptions> options)
        {
            _engineRoot = Path.GetFullPath(options.Value.EngineTopicDirectory);
            // Unconfigured (not composed through DataPaths) → no world layer; authoring refuses to write.
            WorldRoot = string.IsNullOrWhiteSpace(options.Value.WorldTopicDirectory)
                ? string.Empty
                : Path.GetFullPath(options.Value.WorldTopicDirectory);
        }

        public string WorldRoot { get; }

        public IReadOnlyList<HelpLayerRead> ReadLayers() => new[]
        {
            ReadLayer(HelpLayer.Engine, _engineRoot),
            ReadLayer(HelpLayer.World, WorldRoot),
        };

        private static HelpLayerRead ReadLayer(HelpLayer layer, string root)
        {
            if (root.Length == 0 || !Directory.Exists(root))
                return new HelpLayerRead(layer, root, Exists: false, Files: new List<HelpTopicFileRead>());

            var directory = new YamlDefinitionDirectory<HelpTopicDocument>(root);
            var files = directory.List(doc => doc.Id)
                .OrderBy(entry => entry.FileName, System.StringComparer.Ordinal)
                .Select(entry =>
                {
                    if (entry.Error is not null)
                        return new HelpTopicFileRead(entry.FileName, null, entry.Error);
                    var read = directory.Read(entry.Path);
                    return new HelpTopicFileRead(entry.FileName, read.Dto, read.Error);
                })
                .ToList();

            return new HelpLayerRead(layer, root, Exists: true, files);
        }
    }
}
