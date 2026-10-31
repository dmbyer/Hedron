using Hedron.Core.Modules.Help;

namespace Hedron.Core.Output
{
    /// <summary>
    /// The admin-only origin footer after a help topic (R8): <c>[engine topic]</c>, <c>[world topic]</c>
    /// or <c>[world topic — overrides engine]</c>. A separate message written only to administrators,
    /// so a player's output never carries the origin.
    /// </summary>
    public sealed record HelpTopicOriginMessage(HelpTopicOrigin Origin) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Help;

        public string Label => Origin switch
        {
            HelpTopicOrigin.Engine => "[engine topic]",
            HelpTopicOrigin.World => "[world topic]",
            _ => "[world topic — overrides engine]",
        };
    }
}
