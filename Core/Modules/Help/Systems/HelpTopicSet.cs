using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// One immutable, merged help-topic snapshot: a <see cref="DefinitionRegistry{TKey,TDef}"/> of
    /// topics keyed by canonical id, plus its alias index and id order. Each rebuild constructs a new
    /// set, so rows, aliases and origins always swap together in one reference (INV-31).
    /// </summary>
    public sealed class HelpTopicSet : DefinitionRegistry<string, HelpTopic>
    {
        private readonly Dictionary<string, string> _aliasToId;

        public static HelpTopicSet Empty { get; } = new(Array.Empty<HelpTopic>());

        public HelpTopicSet(IEnumerable<HelpTopic> topics)
            : this(topics.OrderBy(t => t.Id, StringComparer.Ordinal).ToList())
        {
        }

        private HelpTopicSet(List<HelpTopic> ordered)
            : base(ordered, t => t.Id)
        {
            Ordered = ordered;
            _aliasToId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var topic in ordered)
                foreach (var alias in topic.Aliases)
                    _aliasToId.TryAdd(alias, topic.Id);
        }

        /// <summary>Every topic in ordinal id order.</summary>
        public IReadOnlyList<HelpTopic> Ordered { get; }

        /// <summary>Exact lookup by id or alias, case-insensitive.</summary>
        public bool TryResolve(string token, out HelpTopic topic)
        {
            var key = HelpTopicSetBuilder.NormalizeToken(token);
            if (TryGet(key, out topic))
                return true;
            if (_aliasToId.TryGetValue(key, out var id))
                return TryGet(id, out topic);
            topic = null!;
            return false;
        }

        /// <summary>
        /// Distinct topics whose id or an alias starts with <paramref name="prefix"/>
        /// (case-insensitive), in id order.
        /// </summary>
        public IReadOnlyList<HelpTopic> PrefixMatches(string prefix)
        {
            var key = HelpTopicSetBuilder.NormalizeToken(prefix);
            if (key.Length == 0)
                return Array.Empty<HelpTopic>();
            return Ordered
                .Where(t => t.Id.StartsWith(key, StringComparison.Ordinal) ||
                            t.Aliases.Any(a => a.StartsWith(key, StringComparison.Ordinal)))
                .ToList();
        }
    }
}
