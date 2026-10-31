using System.Collections.Generic;

namespace Hedron.Core.Output
{
    /// <summary>
    /// One authored help topic (the <c>help &lt;topic&gt;</c> output). Every property is player-visible:
    /// the topic's origin and source file are deliberately absent (the origin is a separate,
    /// admin-only <see cref="HelpTopicOriginMessage"/>), so a serializing formatter never leaks them.
    /// </summary>
    /// <param name="SeeAlso">Resolved canonical topic ids only.</param>
    public sealed record HelpTopicMessage(
        string Id,
        string Title,
        string Body,
        IReadOnlyList<string> SeeAlso) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Help;
    }
}
