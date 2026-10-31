using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Help;
using Hedron.Core.Modules.Help.Systems;
using Hedron.Core.Systems.Definitions;
using Microsoft.Extensions.Options;
using Xunit;
using static Hedron.Tests.Help.HelpTopicTestKit;

namespace Hedron.Tests.Help
{
    /// <summary>
    /// T7 — system-unit (temp dir) tests for <see cref="HelpTopicReloadParticipant"/> over the real
    /// <see cref="HelpTopicLayerReader"/>: both roots in order, missing/aliased roots, diagnostic
    /// sources, and malformed files never throwing.
    /// </summary>
    public sealed class HelpTopicReloadParticipantTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hedron-help-participant-" + Guid.NewGuid().ToString("N"));

        private string EngineDir => Path.Combine(_root, "engine");
        private string WorldDir => Path.Combine(_root, "world", "help");

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }

        internal sealed class FakeVerbRegistry : IVerbRegistry
        {
            public IReadOnlyCollection<ICommand> AllCommands { get; init; } = Array.Empty<ICommand>();
            public bool TryGetExact(string verb, out ICommand? command) { command = null; return false; }
            public IReadOnlyList<ICommand> GetPrefixCandidates(string verb) => Array.Empty<ICommand>();
        }

        private (HelpTopicReloadParticipant Participant, HelpTopicRegistry Registry) Build(string? worldDir = null)
        {
            var reader = new HelpTopicLayerReader(Options.Create(new HelpOptions
            {
                EngineTopicDirectory = EngineDir,
                WorldTopicDirectory = worldDir ?? WorldDir,
            }));
            var registry = new HelpTopicRegistry();
            return (new HelpTopicReloadParticipant(reader, registry, new Lazy<IVerbRegistry>(() => new FakeVerbRegistry())), registry);
        }

        [Fact]
        public async Task Reads_engine_then_world_and_publishes_the_merged_set()
        {
            WriteTopic(EngineDir, "combat.yaml", "combat", "Engine combat");
            WriteTopic(EngineDir, "death.yaml", "death");
            WriteTopic(WorldDir, "combat.yaml", "combat", "World combat");
            var (participant, registry) = Build();

            var diagnostics = await participant.RebuildAsync();

            Assert.Empty(diagnostics);
            Assert.Equal("World combat", registry.Current.Get("combat").Title);
            Assert.Equal(HelpTopicOrigin.WorldOverride, registry.Current.Get("combat").Origin);
            Assert.Equal(HelpTopicOrigin.Engine, registry.Current.Get("death").Origin);
        }

        [Fact]
        public async Task A_missing_engine_root_warns_and_the_world_layer_still_loads()
        {
            WriteTopic(WorldDir, "lore.yaml", "lore");
            var (participant, registry) = Build();

            var diagnostics = await participant.RebuildAsync();

            Assert.Equal(ContentDiagnosticSeverity.Warning, Assert.Single(diagnostics).Severity);
            Assert.Equal(HelpTopicOrigin.World, Assert.Single(registry.Current.Ordered).Origin);
        }

        [Fact]
        public async Task A_world_root_equal_to_the_engine_root_is_skipped()
        {
            WriteTopic(EngineDir, "combat.yaml", "combat");
            var (participant, registry) = Build(worldDir: EngineDir);

            var diagnostics = await participant.RebuildAsync();

            Assert.Contains("world layer skipped", Assert.Single(diagnostics).Message);
            Assert.Equal(HelpTopicOrigin.Engine, Assert.Single(registry.Current.Ordered).Origin);
        }

        [Fact]
        public async Task Malformed_files_never_throw_and_sources_name_the_layer_and_file()
        {
            Directory.CreateDirectory(WorldDir);
            File.WriteAllText(Path.Combine(WorldDir, "kill.yaml"), "id: [unclosed\n  : : :");
            WriteTopic(EngineDir, "combat.yaml", "combat");
            var (participant, registry) = Build();

            var diagnostics = await participant.RebuildAsync();

            Assert.Equal("world/kill.yaml", Assert.Single(diagnostics).Source);
            Assert.Single(registry.Current.Ordered);
        }
    }
}
