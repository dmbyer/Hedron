using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hedron.Core.Modules.Help;
using Hedron.Core.Modules.Help.Systems;

namespace Hedron.Tests.Help
{
    /// <summary>Shared builders for help-topic tests: in-memory layers and temp-dir topic files.</summary>
    internal static class HelpTopicTestKit
    {
        public static readonly IReadOnlySet<string> NoVerbs = new HashSet<string>();

        public static HelpTopicFileRead TopicFile(string fileName, string? id, string? title = "Title", string? body = "Body text.",
            IEnumerable<string>? aliases = null, IEnumerable<string>? seeAlso = null) =>
            new(fileName, new HelpTopicDocument
            {
                Id = id,
                Title = title,
                Body = body,
                Aliases = aliases?.ToList(),
                SeeAlso = seeAlso?.ToList(),
            }, null);

        public static HelpTopicFileRead Broken(string fileName) => new(fileName, null, "parse failure");

        public static HelpLayerRead EngineLayer(params HelpTopicFileRead[] files) =>
            new(HelpLayer.Engine, Path.Combine(Path.GetTempPath(), "engine-help"), true, files.OrderBy(f => f.FileName, StringComparer.Ordinal).ToList());

        public static HelpLayerRead WorldLayer(params HelpTopicFileRead[] files) =>
            new(HelpLayer.World, Path.Combine(Path.GetTempPath(), "world-help"), true, files.OrderBy(f => f.FileName, StringComparer.Ordinal).ToList());

        public static HelpLayerRead MissingWorldLayer() =>
            new(HelpLayer.World, Path.Combine(Path.GetTempPath(), "world-help-missing"), false, new List<HelpTopicFileRead>());

        public static HelpTopicSet Set(params HelpTopic[] topics) => new(topics);

        public static HelpTopic Topic(string id, string title = "Title", IEnumerable<string>? aliases = null,
            HelpTopicOrigin origin = HelpTopicOrigin.Engine, IEnumerable<string>? seeAlso = null) =>
            new(id, title, (aliases ?? Array.Empty<string>()).ToList(), "Body.", (seeAlso ?? Array.Empty<string>()).ToList(), origin, $"engine/{id}.yaml");

        public static void WriteTopic(string directory, string fileName, string id, string title = "Title", string body = "Body text.", string extra = "")
        {
            Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(Path.Combine(directory, fileName),
                $"id: {id}\ntitle: {title}\n{extra}body: |\n  {body}\n");
        }
    }
}
