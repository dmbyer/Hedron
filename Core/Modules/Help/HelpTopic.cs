using System.Collections.Generic;

namespace Hedron.Core.Modules.Help
{
    /// <summary>Which layer a merged help topic came from.</summary>
    public enum HelpTopicOrigin
    {
        /// <summary>The committed engine layer.</summary>
        Engine,

        /// <summary>A world-only topic (<c>&lt;ContentDirectory&gt;/help/</c>).</summary>
        World,

        /// <summary>A world topic that replaces the engine topic with the same id.</summary>
        WorldOverride,
    }

    /// <summary>The two help-topic layers, in merge order (a later layer wins by id).</summary>
    public enum HelpLayer
    {
        Engine,
        World,
    }

    /// <summary>
    /// One merged, validated help topic — a world-content definition (never an entity, INV-12/23).
    /// </summary>
    /// <param name="Id">Canonical lowercase id, e.g. <c>getting-started</c>.</param>
    /// <param name="Aliases">Extra lookup tokens that survived validation.</param>
    /// <param name="SeeAlso">Resolved canonical ids only; unresolved entries were dropped with a warning.</param>
    /// <param name="SourceFile">The file it was loaded from (editor/diagnostics only; never sent to a player).</param>
    public sealed record HelpTopic(
        string Id,
        string Title,
        IReadOnlyList<string> Aliases,
        string Body,
        IReadOnlyList<string> SeeAlso,
        HelpTopicOrigin Origin,
        string SourceFile);

    /// <summary>
    /// The YAML shape of one topic file (<c>&lt;id&gt;.yaml</c>): <c>id</c>, <c>title</c>,
    /// <c>aliases</c> (optional), <c>seeAlso</c> (optional), <c>body</c> (a block scalar).
    /// </summary>
    public sealed class HelpTopicDocument
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public List<string>? Aliases { get; set; }
        public List<string>? SeeAlso { get; set; }
        public string? Body { get; set; }
    }
}
