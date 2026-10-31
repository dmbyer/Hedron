using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Help;
using Hedron.Core.Modules.Help.Systems;
using Hedron.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Help
{
    /// <summary>
    /// T11 — flow tier over committed content: the real <c>content/help/</c> engine layer builds
    /// against the real composed verb set with zero diagnostics, holds the R11 topics, and keeps
    /// every body free of digits (the qualitative-tunables rule: a number in an engine article is
    /// almost always a tunable that could drift from per-server configuration).
    /// </summary>
    public sealed class EngineHelpTopicsTests
    {
        private static string EngineHelpDirectory()
        {
            var root = DataPaths.FindRoot(AppContext.BaseDirectory);
            var dir = Path.Combine(root, "content", "help");
            Assert.True(Directory.Exists(dir), $"engine help directory not found at '{dir}'.");
            return dir;
        }

        private static IVerbRegistry ComposedVerbs()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:DatabasePath"] = ":memory:",
                ["Paths:DataDirectory"] = Path.Combine(Path.GetTempPath(), "hedron-engine-help-unused"),
            }).Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging();
            services.Register(config);
            return services.BuildServiceProvider().GetRequiredService<IVerbRegistry>();
        }

        private static HelpTopicBuildResult BuildEngineLayer()
        {
            var reader = new HelpTopicLayerReader(Options.Create(new HelpOptions
            {
                EngineTopicDirectory = EngineHelpDirectory(),
                WorldTopicDirectory = Path.Combine(Path.GetTempPath(), "hedron-no-world-help-" + Guid.NewGuid().ToString("N")),
            }));
            var verbs = HelpVerbTokens.From(ComposedVerbs());
            return HelpTopicSetBuilder.Build(reader.ReadLayers(), verbs.All, verbs.AdminOnly);
        }

        [Fact]
        public void Engine_topics_build_with_zero_diagnostics_against_the_real_verb_set()
        {
            var result = BuildEngineLayer();

            Assert.True(result.Diagnostics.Count == 0,
                "engine help topics must be diagnostic-free:\n" + string.Join("\n", result.Diagnostics));
        }

        [Fact]
        public void Engine_layer_holds_the_R11_topics()
        {
            var set = BuildEngineLayer().Set;

            Assert.Equal("Getting started", set.Get("getting-started").Title);
            Assert.Equal("Combat", set.Get("combat").Title);
            Assert.Equal("Death & respawn", set.Get("death").Title);
            Assert.Equal("Shops & money", set.Get("shops").Title);
            Assert.Equal("Progression", set.Get("progression").Title);
            Assert.All(set.Ordered, t => Assert.Equal(HelpTopicOrigin.Engine, t.Origin));
        }

        [Fact]
        public void Engine_topic_bodies_contain_no_digits()
        {
            var offenders = BuildEngineLayer().Set.Ordered
                .Where(t => t.Body.Any(char.IsDigit))
                .Select(t => t.Id)
                .ToList();

            Assert.True(offenders.Count == 0,
                "engine help bodies must stay qualitative about tunables (no digits): " + string.Join(", ", offenders));
        }
    }
}
