using System;
using System.Threading.Tasks;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Help;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Hedron.Tests.Harness;
using Xunit;
using static Hedron.Tests.Help.HelpTopicTestKit;

namespace Hedron.Tests.Help
{
    /// <summary>
    /// T16/T18 — system-unit tests for the telnet rendering of the help-topic messages and the
    /// shared <see cref="HelpTopicRendering"/> mapping: footer strings, omitted empty blocks, and
    /// authored text rendered literally (never interpreted as colour markers).
    /// </summary>
    public sealed class HelpTopicFormattingTests
    {
        private readonly TelnetOutputFormatter _formatter = new(new CurrencyRegistry());

        private sealed class ColorSession : ISession
        {
            public Guid SessionId { get; } = Guid.NewGuid();
            public uint PlayerEntityId => 0;
            public string TransportKey => "telnet";
            public bool SupportsColor => true;
            public Task SendLineAsync(string text) => Task.CompletedTask;
        }

        private string Plain(IOutputMessage message) => _formatter.Format(message, new StubSession());

        [Theory]
        [InlineData(HelpTopicOrigin.Engine, "[engine topic]")]
        [InlineData(HelpTopicOrigin.World, "[world topic]")]
        [InlineData(HelpTopicOrigin.WorldOverride, "[world topic — overrides engine]")]
        public void Origin_footer_strings(HelpTopicOrigin origin, string expected)
        {
            Assert.Equal(expected, Plain(new HelpTopicOriginMessage(origin)));
        }

        [Fact]
        public void Topic_renders_title_body_and_see_also()
        {
            var text = Plain(new HelpTopicMessage("combat", "Combat", "Fight.", new[] { "death", "progression" }));

            Assert.StartsWith("[Combat]", text);
            Assert.Contains("Fight.", text);
            Assert.EndsWith("See also: help death, help progression", text);
        }

        [Fact]
        public void See_also_line_is_omitted_when_empty()
        {
            Assert.DoesNotContain("See also", Plain(new HelpTopicMessage("combat", "Combat", "Fight.", Array.Empty<string>())));
        }

        [Fact]
        public void Topics_block_lists_id_and_title()
        {
            var text = Plain(new HelpTopicIndexMessage(new[] { new HelpTopicIndexEntry("combat", "Combat") }));

            Assert.Contains("=== Topics ===", text);
            Assert.Contains("combat", text);
            Assert.Contains("Combat", text);
        }

        [Fact]
        public void Authored_marker_text_renders_literally_with_and_without_colour()
        {
            var message = new HelpTopicMessage("x", "<error>T</error>", "<system>body</system>", Array.Empty<string>());

            var plain = Plain(message);
            var colored = _formatter.Format(message, new ColorSession());

            Assert.Contains("<error>T</error>", plain);
            Assert.Contains("<system>body</system>", plain);
            Assert.Contains("<system>body</system>", colored);
            Assert.Contains("<error>T</error>", colored);
            Assert.DoesNotContain("\x1B[91m", colored);
        }

        [Fact]
        public void Rendering_carries_only_resolved_see_also_and_no_origin_or_path()
        {
            var topic = Topic("combat", "Combat", seeAlso: new[] { "death" }, origin: HelpTopicOrigin.WorldOverride);

            var message = HelpTopicRendering.ToMessage(topic);

            Assert.Equal(new HelpTopicMessage("combat", "Combat", "Body.", topic.SeeAlso), message);
            Assert.DoesNotContain(typeof(HelpTopicMessage).GetProperties(), p => p.Name is "Origin" or "SourceFile");
        }
    }
}
