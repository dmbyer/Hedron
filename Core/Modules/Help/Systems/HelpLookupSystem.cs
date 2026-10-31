using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Sessions;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// Default <see cref="IHelpLookupSystem"/>. Tiers, first visible match wins:
    /// <list type="number">
    /// <item>exact verb or alias;</item>
    /// <item>exact topic id or alias;</item>
    /// <item>exact ability id or display name;</item>
    /// <item>verb prefix (ambiguous → <see cref="HelpMatchFamily.Verb"/> prompt);</item>
    /// <item>topic prefix (ambiguous → <see cref="HelpMatchFamily.Topic"/> prompt);</item>
    /// <item>ability prefix (ambiguous → not found, as before).</item>
    /// </list>
    /// A multi-word query walks tiers 2, 3, 5, 6 with the whole query, then tiers 1 and 4 with its
    /// first token — so <c>help getting started</c> finds the topic and <c>help kill orc</c> still
    /// shows <c>kill</c>. Stateless; reads <see cref="IHelpTopicRegistry.Current"/> once per call (INV-31).
    /// </summary>
    public sealed class HelpLookupSystem : IHelpLookupSystem
    {
        private readonly Lazy<IVerbRegistry> _verbRegistry;
        private readonly IAuthorizationChecker _authorizationChecker;
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IHelpTopicRegistry _topics;

        public HelpLookupSystem(
            Lazy<IVerbRegistry> verbRegistry,
            IAuthorizationChecker authorizationChecker,
            IAbilityRegistry abilityRegistry,
            IHelpTopicRegistry topics)
        {
            _verbRegistry = verbRegistry;
            _authorizationChecker = authorizationChecker;
            _abilityRegistry = abilityRegistry;
            _topics = topics;
        }

        public HelpIndex BuildIndex(ISession invoker)
        {
            var verbs = _verbRegistry.Value.AllCommands
                .Where(c => IsVisible(c, invoker))
                .OrderBy(c => (int)c.Category).ThenBy(c => c.Name)
                .Select(VerbHelp.From)
                .ToList();
            return new HelpIndex(verbs, _topics.Current.Ordered);
        }

        public HelpResolution Resolve(HelpQuery query, ISession invoker)
        {
            if (query.IsEmpty)
                return new HelpResolution.Index();

            var topics = _topics.Current;

            if (query.IsSingleToken)
            {
                return ExactVerb(query.FirstToken, invoker)
                    ?? ExactTopic(topics, query)
                    ?? ExactAbility(query)
                    ?? PrefixVerb(query.FirstToken, invoker)
                    ?? PrefixTopic(topics, query)
                    ?? PrefixAbility(query)
                    ?? new HelpResolution.NotFound();
            }

            return ExactTopic(topics, query)
                ?? ExactAbility(query)
                ?? PrefixTopic(topics, query)
                ?? PrefixAbility(query)
                ?? ExactVerb(query.FirstToken, invoker)
                ?? PrefixVerb(query.FirstToken, invoker)
                ?? new HelpResolution.NotFound();
        }

        // ── Tiers ─────────────────────────────────────────────────────────────

        private HelpResolution? ExactVerb(string token, ISession invoker)
        {
            if (!_verbRegistry.Value.TryGetExact(token, out var command) || command is null || !IsVisible(command, invoker))
                return null;
            return VerbResult(command, token);
        }

        private HelpResolution? PrefixVerb(string token, ISession invoker)
        {
            var visible = _verbRegistry.Value.GetPrefixCandidates(token).Where(c => IsVisible(c, invoker)).ToList();
            return visible.Count switch
            {
                0 => null,
                1 => VerbResult(visible[0], token),
                _ => new HelpResolution.Ambiguous(HelpMatchFamily.Verb, visible.Select(c => c.Name).ToList()),
            };
        }

        private static HelpResolution? ExactTopic(HelpTopicSet topics, HelpQuery query) =>
            topics.TryResolve(query.TopicKey, out var topic) ? new HelpResolution.Topic(topic) : null;

        private static HelpResolution? PrefixTopic(HelpTopicSet topics, HelpQuery query)
        {
            var matches = topics.PrefixMatches(query.TopicKey);
            return matches.Count switch
            {
                0 => null,
                1 => new HelpResolution.Topic(matches[0]),
                _ => new HelpResolution.Ambiguous(HelpMatchFamily.Topic, matches.Select(t => t.Id).ToList()),
            };
        }

        private HelpResolution? ExactAbility(HelpQuery query)
        {
            if (_abilityRegistry.TryGet(query.Raw.ToLowerInvariant(), out var byId))
                return new HelpResolution.Ability(byId);

            foreach (var id in _abilityRegistry.AllIds)
            {
                if (_abilityRegistry.TryGet(id, out var candidate) &&
                    string.Equals(candidate.Name, query.Raw, StringComparison.OrdinalIgnoreCase))
                    return new HelpResolution.Ability(candidate);
            }
            return null;
        }

        // A unique id or display-name prefix; an ambiguous prefix is "not found" rather than a guess.
        private HelpResolution? PrefixAbility(HelpQuery query)
        {
            AbilityDefinition? matched = null;
            foreach (var id in _abilityRegistry.AllIds)
            {
                if (!_abilityRegistry.TryGet(id, out var candidate)) continue;
                if (id.StartsWith(query.Raw, StringComparison.OrdinalIgnoreCase) ||
                    candidate.Name.StartsWith(query.Raw, StringComparison.OrdinalIgnoreCase))
                {
                    if (matched is not null)
                        return null;
                    matched = candidate;
                }
            }
            return matched is null ? null : new HelpResolution.Ability(matched);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // The skills/spells/abilities catalogs follow the typed token, exactly as before.
        private static HelpResolution VerbResult(ICommand command, string typedToken)
        {
            HelpAbilityCatalog? catalog = typedToken.ToLowerInvariant() switch
            {
                "skills" => new HelpAbilityCatalog(AbilityKind.Skill),
                "spells" => new HelpAbilityCatalog(AbilityKind.Spell),
                "abilities" => new HelpAbilityCatalog(null),
                _ => null,
            };
            return new HelpResolution.Verb(VerbHelp.From(command), catalog);
        }

        private bool IsVisible(ICommand command, ISession invoker) =>
            command.RequiredPrivileges.All(r => _authorizationChecker.IsSatisfied(r, invoker));
    }
}
