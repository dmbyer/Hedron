using System;
using System.IO;
using Hedron.Server;
using Xunit;

namespace Hedron.Tests.Composition
{
    /// <summary>
    /// System-unit tests (temp dir) for <see cref="RunModeConfiguration"/>: the run modes layer
    /// <c>appsettings.{env}.json</c> over <c>appsettings.json</c>, tolerate a missing env file, and
    /// load only the base file with no environment.
    /// </summary>
    public sealed class RunModeConfigurationTests : IDisposable
    {
        private readonly string _dir;

        // Probe keys, not World:*, so a machine-level HEDRON_ override (which correctly wins) cannot skew the layering asserts.
        public RunModeConfigurationTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"hedron-runmodecfg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "appsettings.json"),
                "{ \"RunModeProbe\": { \"Layered\": \"base/\", \"BaseOnly\": \"room.base\" } }");
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Fact]
        public void Environment_file_overrides_base_file()
        {
            File.WriteAllText(Path.Combine(_dir, "appsettings.DevWorld.json"),
                "{ \"RunModeProbe\": { \"Layered\": \"dev/\" } }");

            var config = RunModeConfiguration.Build(_dir, "DevWorld");

            Assert.Equal("dev/", config["RunModeProbe:Layered"]);
            Assert.Equal("room.base", config["RunModeProbe:BaseOnly"]);
        }

        [Fact]
        public void Missing_environment_file_is_tolerated()
        {
            var config = RunModeConfiguration.Build(_dir, "NoSuchEnvironment");

            Assert.Equal("base/", config["RunModeProbe:Layered"]);
        }

        [Fact]
        public void Null_environment_loads_the_base_file_only()
        {
            File.WriteAllText(Path.Combine(_dir, "appsettings.DevWorld.json"),
                "{ \"RunModeProbe\": { \"Layered\": \"dev/\" } }");

            var config = RunModeConfiguration.Build(_dir, null);

            Assert.Equal("base/", config["RunModeProbe:Layered"]);
        }

        [Fact]
        public void Host_sources_layer_command_line_over_build_output_json()
        {
            File.WriteAllText(Path.Combine(_dir, "appsettings.DevWorld.json"),
                "{ \"RunModeProbe\": { \"Layered\": \"dev/\", \"EnvOnly\": \"dev\" } }");
            var builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder();
            // A stale source the host would otherwise inherit must be cleared.
            Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(
                builder, new System.Collections.Generic.Dictionary<string, string?> { ["RunModeProbe:Stale"] = "x" });

            RunModeConfiguration.ConfigureHost(builder, _dir, "DevWorld", new[] { "--RunModeProbe:Layered=cli/" });
            var config = builder.Build();

            Assert.Equal("cli/", config["RunModeProbe:Layered"]);
            Assert.Equal("dev", config["RunModeProbe:EnvOnly"]);
            Assert.Equal("room.base", config["RunModeProbe:BaseOnly"]);
            Assert.Null(config["RunModeProbe:Stale"]);
        }
    }
}
