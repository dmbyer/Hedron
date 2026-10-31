using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Help;
using Hedron.Core.Modules.Help.Systems;
using Microsoft.Extensions.Options;
using Xunit;
using static Hedron.Tests.Help.HelpTopicTestKit;

namespace Hedron.Tests.Help
{
    /// <summary>
    /// T17 — system-unit (temp dirs) tests for <see cref="HelpTopicAuthoring"/> (R12): listing with
    /// origins and file errors, world-only writes, refusals, override/delete round-trip, renames,
    /// verb warnings, write serialization — and the engine directory left byte-identical throughout.
    /// </summary>
    public sealed class HelpTopicAuthoringTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hedron-help-authoring-" + Guid.NewGuid().ToString("N"));
        private string EngineDir => Path.Combine(_root, "engine");
        private string WorldDir => Path.Combine(_root, "content", "help");

        public HelpTopicAuthoringTests()
        {
            WriteTopic(EngineDir, "combat.yaml", "combat", "Combat", "Engine combat body.");
            WriteTopic(EngineDir, "death.yaml", "death", "Death", "Engine death body.");
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }

        private HelpTopicAuthoring Authoring(params ICommand[] verbs) => new(
            new HelpTopicLayerReader(Options.Create(new HelpOptions { EngineTopicDirectory = EngineDir, WorldTopicDirectory = WorldDir })),
            new Lazy<IVerbRegistry>(() => new FakeVerbRegistry(verbs)));

        private string EngineSnapshot() => string.Join("|", Directory.GetFiles(EngineDir).OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => Path.GetFileName(f) + ":" + File.ReadAllText(f)));

        private static HelpTopicDraft Draft(string id, string title = "Title", string body = "Body.", string[]? aliases = null, string[]? seeAlso = null) =>
            new(id, title, aliases ?? Array.Empty<string>(), seeAlso ?? Array.Empty<string>(), body);

        [Fact]
        public void List_shows_origins_and_file_errors()
        {
            Directory.CreateDirectory(WorldDir);
            File.WriteAllText(Path.Combine(WorldDir, "broken.yaml"), "id: [unclosed\n  : : :");
            WriteTopic(WorldDir, "lore.yaml", "lore");

            var view = Authoring().List();

            Assert.Equal(new[] { "combat", "death", "lore" }, view.Topics.Select(t => t.Id));
            Assert.Equal(HelpTopicOrigin.World, view.Topics.Single(t => t.Id == "lore").Origin);
            Assert.Contains(view.Diagnostics, d => d.Source == "world/broken.yaml");
        }

        [Fact]
        public async Task Save_writes_the_world_layer_only()
        {
            var before = EngineSnapshot();

            var result = await Authoring().SaveAsync(Draft("lore", "Lore", "Old stories."));

            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(WorldDir, "lore.yaml")));
            Assert.Equal(before, EngineSnapshot());
            var loaded = Authoring().Load("lore")!;
            Assert.Equal("Old stories.", loaded.Topic.Body);
            Assert.True(loaded.Editable);
        }

        [Fact]
        public async Task An_invalid_draft_is_refused_with_nothing_written()
        {
            var result = await Authoring().SaveAsync(Draft("Bad Id!", title: " "));

            Assert.False(result.Success);
            Assert.Equal(2, result.Errors.Count);
            Assert.False(Directory.Exists(WorldDir));
        }

        [Fact]
        public async Task Save_returns_R10_warnings_for_the_topic()
        {
            var result = await Authoring(new FakeCommand("kill")).SaveAsync(Draft("kill"));

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, w => w.Contains("also a command"));
        }

        [Fact]
        public async Task Override_copies_an_engine_topic_and_refuses_a_second_time()
        {
            var before = EngineSnapshot();
            var authoring = Authoring();

            var first = await authoring.OverrideAsync("combat");
            var second = await authoring.OverrideAsync("combat");

            Assert.True(first.Success);
            Assert.False(second.Success);
            var view = authoring.Load("combat")!;
            Assert.Equal(HelpTopicOrigin.WorldOverride, view.Topic.Origin);
            Assert.True(view.OverridesEngine);
            Assert.Equal("Engine combat body.", view.Topic.Body);
            Assert.Equal(before, EngineSnapshot());
        }

        [Fact]
        public async Task Deleting_an_override_restores_the_engine_version()
        {
            var authoring = Authoring();
            await authoring.OverrideAsync("combat");
            await authoring.SaveAsync(Draft("combat", "Combat", "World body."));

            var deleted = await authoring.DeleteAsync("combat");

            Assert.True(deleted.Success);
            var view = authoring.Load("combat")!;
            Assert.Equal(HelpTopicOrigin.Engine, view.Topic.Origin);
            Assert.False(view.Editable);
        }

        [Fact]
        public async Task Engine_topics_cannot_be_deleted()
        {
            var before = EngineSnapshot();

            var result = await Authoring().DeleteAsync("death");

            Assert.False(result.Success);
            Assert.Equal(before, EngineSnapshot());
        }

        [Fact]
        public async Task Renaming_on_save_deletes_the_old_file()
        {
            var authoring = Authoring();
            await authoring.SaveAsync(Draft("old-name"));

            var result = await authoring.SaveAsync(Draft("new-name"), previousId: "old-name");

            Assert.True(result.Success);
            Assert.False(File.Exists(Path.Combine(WorldDir, "old-name.yaml")));
            Assert.True(File.Exists(Path.Combine(WorldDir, "new-name.yaml")));
            Assert.Null(authoring.Load("old-name"));
        }

        [Fact]
        public async Task A_topic_loaded_from_a_differently_named_file_is_consolidated_on_save()
        {
            WriteTopic(WorldDir, "misnamed.yaml", "lore");
            var authoring = Authoring();

            await authoring.SaveAsync(Draft("lore", "Lore", "Rewritten."));

            Assert.False(File.Exists(Path.Combine(WorldDir, "misnamed.yaml")));
            Assert.Equal("Rewritten.", authoring.Load("lore")!.Topic.Body);
        }

        [Fact]
        public void Preview_renders_the_draft_with_resolved_see_also()
        {
            var message = Authoring().Preview(Draft("Lore", "Lore", "Body.", seeAlso: new[] { "combat", "nowhere" }));

            Assert.Equal("lore", message.Id);
            Assert.Equal(new[] { "combat" }, message.SeeAlso);
        }

        [Fact]
        public async Task An_unconfigured_world_root_lists_engine_topics_and_refuses_writes()
        {
            var authoring = new HelpTopicAuthoring(
                new HelpTopicLayerReader(Options.Create(new HelpOptions { EngineTopicDirectory = EngineDir })),
                new Lazy<IVerbRegistry>(() => new FakeVerbRegistry()));

            Assert.Equal(new[] { "combat", "death" }, authoring.List().Topics.Select(t => t.Id));
            Assert.False((await authoring.SaveAsync(Draft("lore"))).Success);
            Assert.False((await authoring.OverrideAsync("combat")).Success);
        }

        [Fact]
        public void Preview_matches_the_help_rendering_of_the_saved_topic()
        {
            var draft = Draft("combat", "Combat", "Engine combat body.");

            Assert.Equal(
                HelpTopicRendering.ToMessage(Authoring().Load("combat")!.Topic) with { SeeAlso = Array.Empty<string>() },
                Authoring().Preview(draft) with { SeeAlso = Array.Empty<string>() });
        }

        [Fact]
        public async Task Concurrent_saves_are_serialized_and_both_land()
        {
            var authoring = Authoring();

            var results = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(i => authoring.SaveAsync(Draft($"topic-{i}"))));

            Assert.All(results, r => Assert.True(r.Success));
            Assert.Equal(8, Directory.GetFiles(WorldDir, "*.yaml").Length);
            Assert.False(Directory.GetFiles(WorldDir, "*.tmp").Any());
        }
    }
}
