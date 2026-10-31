using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Modules.Help;
using Hedron.Core.Modules.Help.Systems;
using Hedron.Core.Systems.Definitions;
using Xunit;
using static Hedron.Tests.Help.HelpTopicTestKit;

namespace Hedron.Tests.Help
{
    /// <summary>
    /// T5 — system-unit tests for the pure <see cref="HelpTopicSetBuilder"/>: per-file validation
    /// (R9), layer merge and origins, the broken-override fallback, and the R10 warnings.
    /// </summary>
    public sealed class HelpTopicSetBuilderTests
    {
        private static HelpTopicBuildResult Build(params HelpLayerRead[] layers) =>
            HelpTopicSetBuilder.Build(layers, NoVerbs, NoVerbs);

        private static IEnumerable<ContentDiagnostic> Errors(HelpTopicBuildResult r) =>
            r.Diagnostics.Where(d => d.Severity == ContentDiagnosticSeverity.Error);

        private static IEnumerable<ContentDiagnostic> Warnings(HelpTopicBuildResult r) =>
            r.Diagnostics.Where(d => d.Severity == ContentDiagnosticSeverity.Warning);

        [Fact]
        public void A_valid_file_loads_with_its_fields()
        {
            var r = Build(EngineLayer(TopicFile("combat.yaml", "combat", "Combat", "Fight things.", aliases: new[] { "fight" })));

            var topic = Assert.Single(r.Set.Ordered);
            Assert.Equal("combat", topic.Id);
            Assert.Equal("Combat", topic.Title);
            Assert.Equal("Fight things.", topic.Body);
            Assert.Equal(new[] { "fight" }, topic.Aliases);
            Assert.Equal("engine/combat.yaml", topic.SourceFile);
            Assert.Empty(r.Diagnostics);
        }

        [Theory]
        [InlineData(null, "T", "B", "id")]
        [InlineData("x", null, "B", "title")]
        [InlineData("x", "T", "  ", "body")]
        public void Missing_required_fields_are_errors_and_skipped(string? id, string? title, string? body, string field)
        {
            var r = Build(EngineLayer(TopicFile("x.yaml", id, title, body)));

            Assert.Empty(r.Set.Ordered);
            var error = Assert.Single(Errors(r));
            Assert.Contains(field, error.Message);
            Assert.Equal("engine/x.yaml", error.Source);
        }

        [Fact]
        public void An_unparseable_file_is_an_error_and_skipped()
        {
            var r = Build(EngineLayer(Broken("bad.yaml")));

            Assert.Empty(r.Set.Ordered);
            Assert.Equal("engine/bad.yaml", Assert.Single(Errors(r)).Source);
        }

        [Fact]
        public void An_invalid_id_is_an_error_and_an_uppercase_id_is_normalized()
        {
            var r = Build(EngineLayer(TopicFile("a.yaml", "bad_id!"), TopicFile("b.yaml", " Getting-Started ")));

            Assert.Equal("getting-started", Assert.Single(r.Set.Ordered).Id);
            Assert.Contains("invalid topic id", Assert.Single(Errors(r)).Message);
        }

        [Fact]
        public void An_invalid_alias_is_dropped_with_a_warning()
        {
            var r = Build(EngineLayer(TopicFile("a.yaml", "alpha", aliases: new[] { "ok", "not ok" })));

            Assert.Equal(new[] { "ok" }, Assert.Single(r.Set.Ordered).Aliases);
            Assert.Contains("invalid alias", Assert.Single(Warnings(r)).Message);
        }

        [Fact]
        public void Origins_engine_world_and_override()
        {
            var r = Build(
                EngineLayer(TopicFile("combat.yaml", "combat", "Engine combat"), TopicFile("death.yaml", "death")),
                WorldLayer(TopicFile("combat.yaml", "combat", "World combat"), TopicFile("lore.yaml", "lore")));

            Assert.Equal(HelpTopicOrigin.WorldOverride, r.Set.Get("combat").Origin);
            Assert.Equal("World combat", r.Set.Get("combat").Title);
            Assert.Equal("world/combat.yaml", r.Set.Get("combat").SourceFile);
            Assert.Equal(HelpTopicOrigin.Engine, r.Set.Get("death").Origin);
            Assert.Equal(HelpTopicOrigin.World, r.Set.Get("lore").Origin);
        }

        [Fact]
        public void A_broken_world_override_serves_the_engine_version_and_says_so()
        {
            var r = Build(EngineLayer(TopicFile("combat.yaml", "combat", "Engine combat")), WorldLayer(Broken("combat.yaml")));

            var topic = r.Set.Get("combat");
            Assert.Equal(HelpTopicOrigin.Engine, topic.Origin);
            Assert.Equal("Engine combat", topic.Title);
            Assert.Equal("world/combat.yaml", Assert.Single(Errors(r)).Source);
            Assert.Contains(Warnings(r), w => w.Source == "world/combat.yaml" && w.Message.Contains("engine topic 'combat' is served"));
        }

        [Fact]
        public void A_duplicate_id_in_one_layer_keeps_the_ordinal_first_file()
        {
            var r = Build(EngineLayer(TopicFile("b.yaml", "same", "From B"), TopicFile("a.yaml", "same", "From A")));

            Assert.Equal("From A", r.Set.Get("same").Title);
            var warning = Assert.Single(Warnings(r));
            Assert.Equal("engine/b.yaml", warning.Source);
            Assert.Contains("duplicate", warning.Message);
        }

        [Fact]
        public void An_alias_equal_to_another_topics_id_is_dropped()
        {
            var r = Build(EngineLayer(TopicFile("a.yaml", "alpha", aliases: new[] { "beta" }), TopicFile("b.yaml", "beta")));

            Assert.Empty(r.Set.Get("alpha").Aliases);
            Assert.True(r.Set.TryResolve("beta", out var resolved));
            Assert.Equal("beta", resolved.Id);
            Assert.Contains("another topic's id", Assert.Single(Warnings(r)).Message);
        }

        [Fact]
        public void A_shared_alias_stays_with_the_ordinal_first_topic()
        {
            var r = Build(EngineLayer(TopicFile("a.yaml", "alpha", aliases: new[] { "shared" }), TopicFile("b.yaml", "beta", aliases: new[] { "shared" })));

            Assert.Equal(new[] { "shared" }, r.Set.Get("alpha").Aliases);
            Assert.Empty(r.Set.Get("beta").Aliases);
            Assert.Contains("already used by topic 'alpha'", Assert.Single(Warnings(r)).Message);
        }

        [Fact]
        public void A_topic_token_equal_to_a_verb_warns_with_admin_wording_for_admin_verbs()
        {
            var verbs = new HashSet<string> { "kill", "reload" };
            var admin = new HashSet<string> { "reload" };

            var r = HelpTopicSetBuilder.Build(
                new[] { EngineLayer(TopicFile("a.yaml", "kill"), TopicFile("b.yaml", "maintenance", aliases: new[] { "reload" })) },
                verbs, admin);

            Assert.Equal(2, r.Set.Ordered.Count);
            var warnings = Warnings(r).ToList();
            Assert.Equal(2, warnings.Count);
            Assert.DoesNotContain("for administrators", warnings.Single(w => w.Message.Contains("'kill'")).Message);
            Assert.Contains("for administrators", warnings.Single(w => w.Message.Contains("'reload'")).Message);
        }

        [Fact]
        public void SeeAlso_resolves_aliases_to_ids_and_drops_unknown_entries()
        {
            var r = Build(EngineLayer(
                TopicFile("a.yaml", "alpha", seeAlso: new[] { "b-alias", "nowhere", "alpha" }),
                TopicFile("b.yaml", "beta", aliases: new[] { "b-alias" })));

            Assert.Equal(new[] { "beta" }, r.Set.Get("alpha").SeeAlso);
            Assert.Contains("seeAlso 'nowhere'", Assert.Single(Warnings(r)).Message);
        }

        [Fact]
        public void Topics_are_ordered_by_id()
        {
            var r = Build(EngineLayer(TopicFile("1.yaml", "zeta"), TopicFile("2.yaml", "alpha"), TopicFile("3.yaml", "mid")));

            Assert.Equal(new[] { "alpha", "mid", "zeta" }, r.Set.Ordered.Select(t => t.Id));
        }

        [Fact]
        public void A_missing_engine_root_warns_and_a_missing_world_root_is_silent()
        {
            var missingEngine = new HelpLayerRead(HelpLayer.Engine, "/nope", false, new List<HelpTopicFileRead>());

            var r = Build(missingEngine, MissingWorldLayer());

            Assert.Empty(r.Set.Ordered);
            Assert.Equal("engine", Assert.Single(r.Diagnostics).Source);
        }

        [Fact]
        public void A_world_root_equal_to_the_engine_root_is_skipped_with_a_warning()
        {
            var engine = EngineLayer(TopicFile("a.yaml", "alpha"));
            var world = new HelpLayerRead(HelpLayer.World, engine.Root, true, engine.Files);

            var r = Build(engine, world);

            Assert.Equal(HelpTopicOrigin.Engine, Assert.Single(r.Set.Ordered).Origin);
            Assert.Contains("world layer skipped", Assert.Single(Warnings(r)).Message);
        }
    }
}
