using System;
using System.Collections.Generic;
using System.IO;
using Hedron.Core.Modules.BalanceInspection;
using Hedron.Core.Modules.Help;
using Hedron.Core.Modules.Persistence;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.World;
using Hedron.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Composition
{
    /// <summary>
    /// System-unit tests (temp dirs) for <see cref="DataPaths"/> — the single data-location rule:
    /// everything under <c>Paths:DataDirectory</c>, per-location keys override, and relative paths
    /// resolve against the Hedron root (the checkout holding <c>Hedron.sln</c>, else the install folder),
    /// never the working directory.
    /// </summary>
    public sealed class DataPathsTests : IDisposable
    {
        private readonly string _checkout;
        private readonly string _appBase;

        public DataPathsTests()
        {
            _checkout = Path.Combine(Path.GetTempPath(), $"hedron-datapaths-{Guid.NewGuid():N}");
            _appBase = Path.Combine(_checkout, "Server", "bin", "Debug", "net8.0");
            Directory.CreateDirectory(_appBase);
            File.WriteAllText(Path.Combine(_checkout, DataPaths.SolutionFileName), "");
        }

        public void Dispose()
        {
            if (Directory.Exists(_checkout)) Directory.Delete(_checkout, recursive: true);
        }

        private IServiceProvider Compose(Dictionary<string, string?> settings, string? appBase = null)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            services.AddOptions();
            services.Configure<WorldOptions>(config.GetSection("World"));
            services.Configure<PersistenceOptions>(config.GetSection("Persistence"));
            services.Configure<BalanceOptions>(config.GetSection("Balance"));
            services.Configure<SimulationOptions>(config.GetSection("Simulation"));
            services.Configure<HelpOptions>(config.GetSection("Help"));
            services.AddDataPaths(config, appBase ?? _appBase);
            return services.BuildServiceProvider();
        }

        private static T Get<T>(IServiceProvider sp) where T : class => sp.GetRequiredService<IOptions<T>>().Value;

        [Fact]
        public void Root_is_the_checkout_holding_the_solution_file()
        {
            Assert.Equal(Path.GetFullPath(_checkout), DataPaths.FindRoot(_appBase));
        }

        [Fact]
        public void Root_is_the_install_folder_when_no_solution_file_is_above_it()
        {
            var install = Path.Combine(Path.GetTempPath(), $"hedron-install-{Guid.NewGuid():N}");
            Directory.CreateDirectory(install);
            try
            {
                Assert.Equal(Path.GetFullPath(install), DataPaths.FindRoot(install));
            }
            finally
            {
                Directory.Delete(install);
            }
        }

        [Fact]
        public void Every_location_derives_from_the_default_data_directory_under_the_root()
        {
            var sp = Compose(new());
            var data = Path.Combine(_checkout, "data");

            Assert.Equal(Path.Combine(data, "content"), Get<WorldOptions>(sp).ContentDirectory);
            Assert.Equal(Path.Combine(data, "hedron.db"), Get<PersistenceOptions>(sp).DatabasePath);
            Assert.Equal(Path.Combine(data, "balance", "standards.yaml"), Get<BalanceOptions>(sp).StandardsPath);
            Assert.Equal(Path.Combine(data, "sim", "reports"), Get<SimulationOptions>(sp).ReportDirectory);
            Assert.Equal(Path.Combine(data, "sim", "scenarios"), Get<SimulationOptions>(sp).ScenarioDirectory);
        }

        [Fact]
        public void An_absolute_data_directory_moves_every_location()
        {
            var elsewhere = Path.Combine(Path.GetTempPath(), "hedron-world-elsewhere");
            var sp = Compose(new() { ["Paths:DataDirectory"] = elsewhere });

            Assert.Equal(Path.Combine(elsewhere, "content"), Get<WorldOptions>(sp).ContentDirectory);
            Assert.Equal(Path.Combine(elsewhere, "hedron.db"), Get<PersistenceOptions>(sp).DatabasePath);
        }

        [Fact]
        public void A_per_location_key_overrides_the_data_directory_and_resolves_against_the_root()
        {
            // The DevWorld profile's shape, with a machine-level data directory also set: the
            // explicit locations win, so the profile works without unsetting anything.
            var sp = Compose(new()
            {
                ["Paths:DataDirectory"] = Path.Combine(Path.GetTempPath(), "hedron-world-elsewhere"),
                ["World:ContentDirectory"] = "content/dev-world/",
                ["Persistence:DatabasePath"] = "data/dev-world.db",
            });

            Assert.Equal(Path.GetFullPath(Path.Combine(_checkout, "content/dev-world/")), Get<WorldOptions>(sp).ContentDirectory);
            Assert.Equal(Path.GetFullPath(Path.Combine(_checkout, "data/dev-world.db")), Get<PersistenceOptions>(sp).DatabasePath);
        }

        [Fact]
        public void Sqlite_uris_and_absolute_paths_pass_through_unchanged()
        {
            var absolute = Path.Combine(Path.GetTempPath(), "abs-content");
            var sp = Compose(new()
            {
                ["Persistence:DatabasePath"] = ":memory:",
                ["World:ContentDirectory"] = absolute,
            });

            Assert.Equal(":memory:", Get<PersistenceOptions>(sp).DatabasePath);
            Assert.Equal(absolute, Get<WorldOptions>(sp).ContentDirectory);
            Assert.Equal("file:x?mode=memory", DataPaths.Resolve(_checkout, "file:x?mode=memory"));
        }

        // ── help-topics T10: the engine help layer is install-relative ────────────

        [Fact]
        public void Engine_help_directory_defaults_to_content_help_under_the_root()
        {
            var help = Get<HelpOptions>(Compose(new()));

            Assert.Equal(Path.GetFullPath(Path.Combine(_checkout, "content", "help")), help.EngineTopicDirectory);
        }

        [Fact]
        public void Engine_help_directory_relative_resolves_against_the_root_and_absolute_is_kept()
        {
            var relative = Get<HelpOptions>(Compose(new() { ["Help:EngineTopicDirectory"] = "engine/help" }));
            var absoluteDir = Path.Combine(Path.GetTempPath(), "hedron-abs-help");
            var absolute = Get<HelpOptions>(Compose(new() { ["Help:EngineTopicDirectory"] = absoluteDir }));

            Assert.Equal(Path.GetFullPath(Path.Combine(_checkout, "engine", "help")), relative.EngineTopicDirectory);
            Assert.Equal(absoluteDir, absolute.EngineTopicDirectory);
        }

        [Fact]
        public void Data_directory_moves_the_world_help_layer_but_not_the_engine_layer()
        {
            var help = Get<HelpOptions>(Compose(new() { ["Paths:DataDirectory"] = "elsewhere" }));

            Assert.Equal(Path.GetFullPath(Path.Combine(_checkout, "content", "help")), help.EngineTopicDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(Path.Combine(_checkout, "elsewhere")), "content", "help"), help.WorldTopicDirectory);
        }

        [Fact]
        public void World_help_layer_follows_World_ContentDirectory()
        {
            var help = Get<HelpOptions>(Compose(new() { ["World:ContentDirectory"] = "content/dev-world" }));

            Assert.Equal(Path.Combine(Path.GetFullPath(Path.Combine(_checkout, "content", "dev-world")), "help"), help.WorldTopicDirectory);
        }
    }
}
