using System.Collections.Generic;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>One layer as read from disk, before validation and merge.</summary>
    /// <param name="Root">The layer's directory (full path).</param>
    /// <param name="Exists">Whether the directory exists.</param>
    /// <param name="Files">Every <c>*.yaml</c> file in ordinal file-name order.</param>
    public sealed record HelpLayerRead(HelpLayer Layer, string Root, bool Exists, IReadOnlyList<HelpTopicFileRead> Files);

    /// <summary>One topic file as read: its document, or the parse error that kept it from loading.</summary>
    public sealed record HelpTopicFileRead(string FileName, HelpTopicDocument? Document, string? Error);
}
