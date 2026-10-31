using System;
using System.Linq;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Help;
using Hedron.Core.Modules.Help.Systems;
using Hedron.Tests.Harness;
using Xunit;
using static Hedron.Tests.Help.HelpTopicTestKit;

namespace Hedron.Tests.Help
{
    /// <summary>
    /// T14 — system-unit tests for <see cref="HelpLookupSystem"/> (R5/R6): each tier, exact before
    /// prefix, the visibility gate, ambiguity per family, the multi-word fallback, and the ability
    /// catalogs. Also the on-touch ratchet for the lookup that used to live untested in <c>HelpCommand</c>.
    /// </summary>
    public sealed class HelpLookupSystemTests
    {
        private readonly StubSession _player = new(1);
        private readonly StubSession _admin = new(2);
        private readonly HelpTopicRegistry _topics = new();

        private HelpLookupSystem Lookup(params ICommand[] commands) =>
            new(new Lazy<IVerbRegistry>(() => new FakeVerbRegistry(commands)),
                new FakeAuthorization(_admin), new AbilityRegistry(), _topics);

        private HelpResolution Resolve(HelpLookupSystem lookup, string input, StubSession? who = null) =>
            lookup.Resolve(HelpQuery.Parse(input), who ?? _player);

        private static string VerbName(HelpResolution r) => Assert.IsType<HelpResolution.Verb>(r).Help.Name;
        private static string TopicId(HelpResolution r) => Assert.IsType<HelpResolution.Topic>(r).Help.Id;

        [Fact]
        public void Empty_query_is_the_index()
        {
            Assert.IsType<HelpResolution.Index>(Resolve(Lookup(), "  "));
        }

        [Fact]
        public void Exact_verb_and_alias()
        {
            var lookup = Lookup(new FakeCommand("look", false, "l"));

            Assert.Equal("look", VerbName(Resolve(lookup, "look")));
            Assert.Equal("look", VerbName(Resolve(lookup, "L")));
        }

        [Fact]
        public void Exact_verb_beats_exact_topic()
        {
            _topics.Publish(Set(Topic("look")));

            Assert.Equal("look", VerbName(Resolve(Lookup(new FakeCommand("look")), "look")));
        }

        [Fact]
        public void Exact_topic_beats_a_verb_prefix()
        {
            _topics.Publish(Set(Topic("combat")));

            Assert.Equal("combat", TopicId(Resolve(Lookup(new FakeCommand("combatlog")), "combat")));
        }

        [Fact]
        public void Exact_topic_by_alias()
        {
            _topics.Publish(Set(Topic("death", aliases: new[] { "respawn" })));

            Assert.Equal("death", TopicId(Resolve(Lookup(), "respawn")));
        }

        [Fact]
        public void An_admin_only_verb_is_invisible_to_players_so_the_same_named_topic_resolves()
        {
            _topics.Publish(Set(Topic("reload")));
            var lookup = Lookup(new FakeCommand("reload", adminOnly: true));

            Assert.Equal("reload", TopicId(Resolve(lookup, "reload", _player)));
            Assert.Equal("reload", VerbName(Resolve(lookup, "reload", _admin)));
        }

        [Fact]
        public void Exact_ability_by_id_and_display_name()
        {
            var lookup = Lookup();

            Assert.Equal("kick", Assert.IsType<HelpResolution.Ability>(Resolve(lookup, "kick")).Definition.Id);
            Assert.Equal("kick", Assert.IsType<HelpResolution.Ability>(Resolve(lookup, "KICK")).Definition.Id);
        }

        [Fact]
        public void Verb_prefix_unique_resolves_and_ambiguous_lists_visible_candidates_only()
        {
            var lookup = Lookup(new FakeCommand("look"), new FakeCommand("lock"), new FakeCommand("list"), new FakeCommand("loadall", adminOnly: true));

            Assert.Equal("list", VerbName(Resolve(lookup, "li")));
            var ambiguous = Assert.IsType<HelpResolution.Ambiguous>(Resolve(lookup, "lo"));
            Assert.Equal(HelpMatchFamily.Verb, ambiguous.Family);
            Assert.Equal(new[] { "lock", "look" }, ambiguous.Candidates);
        }

        [Fact]
        public void Topic_prefix_unique_resolves_and_ambiguous_is_a_topic_prompt()
        {
            _topics.Publish(Set(Topic("death"), Topic("defense"), Topic("progression")));
            var lookup = Lookup();

            Assert.Equal("progression", TopicId(Resolve(lookup, "prog")));
            var ambiguous = Assert.IsType<HelpResolution.Ambiguous>(Resolve(lookup, "de"));
            Assert.Equal(HelpMatchFamily.Topic, ambiguous.Family);
            Assert.Equal(new[] { "death", "defense" }, ambiguous.Candidates);
        }

        [Fact]
        public void Verb_prefix_tier_resolves_before_topic_prefix()
        {
            _topics.Publish(Set(Topic("score-guide")));

            Assert.Equal("score", VerbName(Resolve(Lookup(new FakeCommand("score")), "sco")));
        }

        [Fact]
        public void A_unique_ability_prefix_resolves()
        {
            Assert.Equal("toughness", Assert.IsType<HelpResolution.Ability>(Resolve(Lookup(), "tou")).Definition.Id);
        }

        [Fact]
        public void An_ambiguous_ability_prefix_is_not_found()
        {
            var abilities = new TestAbilityRegistry(
                TestAbilityRegistry.Ability("firebolt", "Fire Bolt"),
                TestAbilityRegistry.Ability("firewall", "Fire Wall"));
            var lookup = new HelpLookupSystem(new Lazy<IVerbRegistry>(() => new FakeVerbRegistry()),
                new FakeAuthorization(), abilities, _topics);

            Assert.IsType<HelpResolution.NotFound>(Resolve(lookup, "fire"));
            Assert.Equal("firebolt", Assert.IsType<HelpResolution.Ability>(Resolve(lookup, "fire bolt")).Definition.Id);
        }

        [Fact]
        public void Multi_word_query_finds_a_topic()
        {
            _topics.Publish(Set(Topic("getting-started")));

            Assert.Equal("getting-started", TopicId(Resolve(Lookup(new FakeCommand("get")), "getting started")));
        }

        [Fact]
        public void A_multi_word_topic_beats_the_verb_named_by_its_first_token()
        {
            _topics.Publish(Set(Topic("get-started")));

            Assert.Equal("get-started", TopicId(Resolve(Lookup(new FakeCommand("get")), "get started")));
        }

        [Fact]
        public void A_multi_word_query_with_no_topic_falls_back_to_its_first_token_verb()
        {
            var lookup = Lookup(new FakeCommand("kill", false, "k"));

            Assert.Equal("kill", VerbName(Resolve(lookup, "kill orc")));
            Assert.Equal("kill", VerbName(Resolve(lookup, "ki orc")));
        }

        [Fact]
        public void Nothing_matching_is_not_found()
        {
            Assert.IsType<HelpResolution.NotFound>(Resolve(Lookup(new FakeCommand("look")), "nothing here"));
        }

        [Theory]
        [InlineData("skills", AbilityKind.Skill)]
        [InlineData("spells", AbilityKind.Spell)]
        public void Typing_a_catalog_verb_appends_its_catalog(string verb, AbilityKind kind)
        {
            var resolution = Assert.IsType<HelpResolution.Verb>(Resolve(Lookup(new FakeCommand(verb)), verb));

            Assert.Equal(kind, resolution.Catalog!.Kind);
        }

        [Fact]
        public void Abilities_catalog_is_all_kinds_and_a_prefix_of_skills_has_no_catalog()
        {
            var lookup = Lookup(new FakeCommand("abilities"), new FakeCommand("skills"));

            Assert.Null(Assert.IsType<HelpResolution.Verb>(Resolve(lookup, "abilities")).Catalog!.Kind);
            Assert.Null(Assert.IsType<HelpResolution.Verb>(Resolve(lookup, "ski")).Catalog);
        }

        [Fact]
        public void Resolve_reads_one_snapshot_per_call()
        {
            _topics.Publish(Set(Topic("alpha")));
            var lookup = Lookup();
            var before = Resolve(lookup, "alpha");

            _topics.Publish(Set(Topic("beta")));

            Assert.Equal("alpha", TopicId(before));
            Assert.IsType<HelpResolution.NotFound>(Resolve(lookup, "alpha"));
        }

        [Fact]
        public void BuildIndex_hides_admin_verbs_from_players_and_orders_topics()
        {
            _topics.Publish(Set(Topic("zeta"), Topic("alpha")));
            var lookup = Lookup(new FakeCommand("look"), new FakeCommand("reload", adminOnly: true));

            var playerIndex = lookup.BuildIndex(_player);
            var adminIndex = lookup.BuildIndex(_admin);

            Assert.Equal(new[] { "look" }, playerIndex.Verbs.Select(v => v.Name));
            Assert.Equal(new[] { "look", "reload" }, adminIndex.Verbs.Select(v => v.Name));
            Assert.Equal(new[] { "alpha", "zeta" }, playerIndex.Topics.Select(t => t.Id));
        }
    }
}
