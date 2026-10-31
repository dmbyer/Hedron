using System.Collections.Generic;

namespace Hedron.Core.Output
{
    /// <summary>One row of the Topics block in the bare-<c>help</c> index.</summary>
    public sealed record HelpTopicIndexEntry(string Id, string Title);

    /// <summary>
    /// The Topics block written after the verb index by bare <c>help</c> (R3), in id order.
    /// Not written when there are no topics.
    /// </summary>
    public sealed record HelpTopicIndexMessage(IReadOnlyList<HelpTopicIndexEntry> Topics) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Help;
    }
}
