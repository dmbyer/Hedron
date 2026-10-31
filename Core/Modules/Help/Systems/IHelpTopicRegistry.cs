using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// The game's current help-topic snapshot (Spine F, string-keyed). Read <see cref="Current"/> once
    /// per operation: it is one complete, immutable set, swapped atomically on every content load.
    /// The <see cref="IRegistry{TKey,TDef}"/> members delegate to <see cref="Current"/>.
    /// </summary>
    public interface IHelpTopicRegistry : IRegistry<string, HelpTopic>
    {
        HelpTopicSet Current { get; }
    }
}
