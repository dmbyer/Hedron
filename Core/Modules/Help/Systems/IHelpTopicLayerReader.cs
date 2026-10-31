using System.Collections.Generic;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// Reads the help-topic layers from disk — the engine root, then the world root — through the
    /// shared <c>YamlDefinitionDirectory</c>. I/O only: no validation or merge (that is
    /// <see cref="HelpTopicSetBuilder"/>). Never throws for a missing directory or unparseable file.
    /// </summary>
    public interface IHelpTopicLayerReader
    {
        /// <summary>The layers in merge order (engine, world).</summary>
        IReadOnlyList<HelpLayerRead> ReadLayers();

        /// <summary>The world layer's directory (where the editor writes).</summary>
        string WorldRoot { get; }
    }
}
