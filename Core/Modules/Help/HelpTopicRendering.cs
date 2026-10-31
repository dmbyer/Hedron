using Hedron.Core.Output;

namespace Hedron.Core.Modules.Help
{
    /// <summary>
    /// The one topic → output mapping, shared by <c>help</c> and the editor's preview so both show
    /// exactly what a player sees. Carries only the resolved <c>seeAlso</c>; never the origin or path.
    /// </summary>
    public static class HelpTopicRendering
    {
        public static HelpTopicMessage ToMessage(HelpTopic topic) =>
            new(topic.Id, topic.Title, topic.Body, topic.SeeAlso);
    }
}
