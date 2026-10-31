using System.Collections.Generic;
using System.Threading;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// Default <see cref="IHelpTopicRegistry"/>. Holds one <see cref="HelpTopicSet"/> reference,
    /// published with <see cref="Volatile.Write{T}(ref T, T)"/>: readers never lock and always see one
    /// complete set (INV-31, guarded by single-reference publication). <see cref="HelpTopicSet.Empty"/>
    /// until the first content load. <see cref="Publish"/> is on the concrete type only — the
    /// reload participant is its one writer.
    /// </summary>
    public sealed class HelpTopicRegistry : IHelpTopicRegistry
    {
        private HelpTopicSet _current = HelpTopicSet.Empty;

        public HelpTopicSet Current => Volatile.Read(ref _current);

        public void Publish(HelpTopicSet set) => Volatile.Write(ref _current, set);

        public bool TryGet(string key, out HelpTopic definition) => Current.TryGet(key, out definition);

        public HelpTopic Get(string key) => Current.Get(key);

        public IReadOnlyCollection<string> AllIds => Current.AllIds;

        public IReadOnlyCollection<HelpTopic> All => Current.All;
    }
}
