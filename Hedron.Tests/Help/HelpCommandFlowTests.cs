using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Help;
using Hedron.Core.Modules.Help.Commands;
using Hedron.Core.Modules.Help.Systems;
using Hedron.Core.Output;
using Hedron.Tests.Harness;
using Hedron.Tests.Spawn;
using Xunit;
using static Hedron.Tests.Help.HelpTopicTestKit;

namespace Hedron.Tests.Help
{
    /// <summary>
    /// T15 — flow tier for <see cref="HelpCommand"/> over the real lookup system: the index plus
    /// Topics block, topic output, the admin-only origin footer, read-only, usable while incapacitated.
    /// </summary>
    public sealed class HelpCommandFlowTests
    {
        private readonly StubSession _player = new(1);
        private readonly StubSession _admin = new(2);
        private readonly HelpTopicRegistry _topics = new();
        private readonly RecordingOutput _output = new();

        private HelpCommand Command()
        {
            var auth = new FakeAuthorization(_admin);
            var verbs = new FakeVerbRegistry(new FakeCommand("look", false, "l"), new FakeCommand("reload", adminOnly: true));
            var lookup = new HelpLookupSystem(new Lazy<IVerbRegistry>(() => verbs), auth, new AbilityRegistry(), _topics);
            return new HelpCommand(lookup, auth, new AbilityRegistry());
        }

        private async Task<IReadOnlyList<IOutputMessage>> RunAsync(StubSession who, string? topic)
        {
            var args = new Dictionary<string, object?>();
            if (topic is not null)
                args["topic"] = topic;
            await Command().ExecuteAsync(new CommandContext(who, who.PlayerEntityId, SpawnWorld.Args(args),
                _output.WriterFor(who.PlayerEntityId), Services: null!));
            return _output.All.Where(r => r.RecipientEntityId == who.PlayerEntityId).Select(r => r.Message).ToList();
        }

        [Fact]
        public async Task Bare_help_writes_the_verb_index_then_the_topics_block()
        {
            _topics.Publish(Set(Topic("combat", "Combat"), Topic("death", "Death & respawn")));

            var messages = await RunAsync(_player, null);

            Assert.Collection(messages,
                m => Assert.Equal(new[] { "look" }, Assert.IsType<HelpIndexMessage>(m).Entries.Select(e => e.Verb)),
                m => Assert.Equal(new[] { "combat", "death" }, Assert.IsType<HelpTopicIndexMessage>(m).Topics.Select(t => t.Id)));
        }

        [Fact]
        public async Task With_no_topics_there_is_no_topics_block()
        {
            var messages = await RunAsync(_player, null);

            Assert.IsType<HelpIndexMessage>(Assert.Single(messages));
        }

        [Fact]
        public async Task A_topic_query_writes_the_topic_and_no_footer_for_a_player()
        {
            _topics.Publish(Set(Topic("getting-started", "Getting started", seeAlso: new[] { "combat" }), Topic("combat")));

            var messages = await RunAsync(_player, "getting started");

            var topic = Assert.IsType<HelpTopicMessage>(Assert.Single(messages));
            Assert.Equal("Getting started", topic.Title);
            Assert.Equal(new[] { "combat" }, topic.SeeAlso);
        }

        [Fact]
        public async Task An_admin_gets_the_origin_footer_after_the_topic()
        {
            _topics.Publish(Set(Topic("combat", origin: HelpTopicOrigin.WorldOverride)));

            var messages = await RunAsync(_admin, "combat");

            Assert.Collection(messages,
                m => Assert.IsType<HelpTopicMessage>(m),
                m => Assert.Equal(HelpTopicOrigin.WorldOverride, Assert.IsType<HelpTopicOriginMessage>(m).Origin));
        }

        [Fact]
        public async Task Verb_help_and_not_found_still_work()
        {
            var verb = await RunAsync(_player, "l");
            Assert.Equal("look", Assert.IsType<HelpEntryMessage>(Assert.Single(verb)).Verb);

            var missing = await RunAsync(_player, "nothing-here");
            Assert.Equal("No help found for 'nothing-here'.", Assert.IsType<PlainMessage>(missing.Last()).Text);
        }

        [Fact]
        public async Task An_ambiguous_topic_prefix_prompts_with_topic_ids()
        {
            _topics.Publish(Set(Topic("death"), Topic("defense")));

            var messages = await RunAsync(_player, "de");

            Assert.Equal("Ambiguous topic 'de'. Did you mean: death, defense?", Assert.IsType<PlainMessage>(Assert.Single(messages)).Text);
        }

        [Fact]
        public void Help_is_usable_while_incapacitated_and_takes_the_rest_of_the_line()
        {
            var command = Command();

            Assert.True(command.UsableWhileIncapacitated);
            Assert.Equal(CommandArgumentKind.RestOfLine, Assert.Single(command.ArgumentSchema.Arguments).Kind);
        }

        [Fact]
        public async Task Help_publishes_nothing()
        {
            // HelpCommand takes no IEventBus at all — read-only by construction.
            Assert.DoesNotContain(typeof(HelpCommand).GetConstructors().Single().GetParameters(),
                p => p.ParameterType == typeof(Hedron.Core.Events.IEventBus));
            _topics.Publish(Set(Topic("combat")));
            Assert.NotEmpty(await RunAsync(_player, "combat"));
        }
    }
}
