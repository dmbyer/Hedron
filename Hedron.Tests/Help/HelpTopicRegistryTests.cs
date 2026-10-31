using Hedron.Core.Modules.Help.Systems;
using Xunit;
using static Hedron.Tests.Help.HelpTopicTestKit;

namespace Hedron.Tests.Help
{
    /// <summary>
    /// T6 — system-unit tests for <see cref="HelpTopicRegistry"/> and <see cref="HelpTopicSet"/>:
    /// empty before the first publish, whole-set swaps, captured snapshots unaffected, lookup.
    /// </summary>
    public sealed class HelpTopicRegistryTests
    {
        [Fact]
        public void Empty_before_the_first_publish()
        {
            var registry = new HelpTopicRegistry();

            Assert.Same(HelpTopicSet.Empty, registry.Current);
            Assert.Empty(registry.AllIds);
        }

        [Fact]
        public void Publish_swaps_the_whole_set_and_a_captured_set_is_unaffected()
        {
            var registry = new HelpTopicRegistry();
            registry.Publish(Set(Topic("alpha", aliases: new[] { "a" })));
            var captured = registry.Current;

            registry.Publish(Set(Topic("beta", aliases: new[] { "b" })));

            Assert.True(captured.TryResolve("a", out _));
            Assert.False(captured.TryResolve("b", out _));
            Assert.False(registry.Current.TryResolve("a", out _));
            Assert.True(registry.Current.TryResolve("b", out _));
        }

        [Fact]
        public void IRegistry_members_delegate_to_the_current_set()
        {
            var registry = new HelpTopicRegistry();
            registry.Publish(Set(Topic("alpha")));

            Assert.True(registry.TryGet("alpha", out var topic));
            Assert.Equal("alpha", topic.Id);
            Assert.Equal("alpha", registry.Get("alpha").Id);
            Assert.Single(registry.All);
        }

        [Fact]
        public void TryResolve_is_case_insensitive_by_id_and_alias_and_prefixes_are_distinct()
        {
            var set = Set(Topic("death", aliases: new[] { "dying", "respawn" }), Topic("defense"));

            Assert.True(set.TryResolve("DEATH", out var byId));
            Assert.Equal("death", byId.Id);
            Assert.True(set.TryResolve("Respawn", out var byAlias));
            Assert.Equal("death", byAlias.Id);
            Assert.Collection(set.PrefixMatches("de"), t => Assert.Equal("death", t.Id), t => Assert.Equal("defense", t.Id));
            Assert.Single(set.PrefixMatches("dy"));
            Assert.Empty(set.PrefixMatches(""));
        }
    }
}
