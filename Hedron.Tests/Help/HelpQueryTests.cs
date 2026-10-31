using Hedron.Core.Modules.Help.Systems;
using Xunit;

namespace Hedron.Tests.Help
{
    /// <summary>T13 — system-unit tests for <see cref="HelpQuery.Parse"/> (R2).</summary>
    public sealed class HelpQueryTests
    {
        [Theory]
        [InlineData("getting started")]
        [InlineData("Getting-Started")]
        [InlineData("getting-started")]
        [InlineData("  GETTING    started  ")]
        public void Spellings_of_a_topic_share_one_key(string input)
        {
            Assert.Equal("getting-started", HelpQuery.Parse(input).TopicKey);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("-")]
        [InlineData(" -- ")]
        public void Blank_or_hyphen_only_input_is_empty(string? input)
        {
            Assert.True(HelpQuery.Parse(input).IsEmpty);
        }

        [Fact]
        public void Single_and_multi_token_and_first_token()
        {
            var single = HelpQuery.Parse("look");
            var multi = HelpQuery.Parse("kill orc");

            Assert.True(single.IsSingleToken);
            Assert.False(multi.IsSingleToken);
            Assert.Equal("kill", multi.FirstToken);
            Assert.Equal("kill orc", multi.Raw);
        }

        [Fact]
        public void One_pair_of_surrounding_quotes_is_stripped()
        {
            var q = HelpQuery.Parse("\"fire bolt\"");

            Assert.Equal("fire bolt", q.Raw);
            Assert.Equal("fire-bolt", q.TopicKey);
            Assert.Equal("fire", q.FirstToken);
        }
    }
}
