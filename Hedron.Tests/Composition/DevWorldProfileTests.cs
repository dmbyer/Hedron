using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hedron.Tests.World;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Hedron.Tests.Composition
{
    /// <summary>
    /// Config-tier guard over the committed <c>DevWorld</c> profiles: the Server and Web
    /// <c>appsettings.DevWorld.json</c> carry the same keys and values, so the editor's Simulation
    /// page and the Server's <c>simulate</c> see the same scenarios, and both hosts grade dev-world
    /// against the same (pinned) balance standards. Path resolution itself is
    /// <see cref="DataPathsTests"/>' job.
    /// </summary>
    public sealed class DevWorldProfileTests
    {
        private static IReadOnlyDictionary<string, string?> Flatten(string relativePath) =>
            new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(DevWorldContentFixture.FindRepoRoot(), relativePath), optional: false)
                .Build()
                .AsEnumerable()
                .Where(kv => kv.Value is not null)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

        private static readonly IReadOnlyDictionary<string, string?> Server = Flatten("Server/appsettings.DevWorld.json");
        private static readonly IReadOnlyDictionary<string, string?> Web = Flatten("Hedron.Web/appsettings.DevWorld.json");

        [Fact]
        public void Server_and_web_devworld_profiles_have_the_same_keys_and_values()
        {
            Assert.Equal(
                Server.OrderBy(kv => kv.Key).ToList(),
                Web.OrderBy(kv => kv.Key).ToList());
        }

        [Fact]
        public void Devworld_profile_points_scenarios_at_the_committed_sim_folder()
        {
            Assert.Equal("content/dev-world/sim", Server["Simulation:ScenarioDirectory"]);
        }

        [Fact]
        public void Devworld_profile_pins_balance_standards_inside_dev_world()
        {
            Assert.Equal("content/dev-world/balance/standards.yaml", Server["Balance:StandardsPath"]);
        }
    }
}
