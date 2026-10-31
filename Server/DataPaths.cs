using System;
using System.IO;
using Hedron.Core.Modules.BalanceInspection;
using Hedron.Core.Modules.Help;
using Hedron.Core.Modules.Help.Systems;
using Hedron.Core.Modules.Persistence;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Server;

/// <summary>
/// The single rule for where Hedron keeps its data. Every host and run mode composes through it,
/// so a path means the same thing whatever the launcher or working directory.
/// <list type="bullet">
/// <item><c>Paths:DataDirectory</c> (default <c>data</c>) holds everything mutable:
/// <c>content/</c>, <c>hedron.db</c>, <c>balance/standards.yaml</c>, <c>sim/reports</c>, <c>sim/scenarios</c>.</item>
/// <item>A per-location key (<c>World:ContentDirectory</c>, <c>Persistence:DatabasePath</c>,
/// <c>Balance:StandardsPath</c>, <c>Simulation:ReportDirectory</c>/<c>ScenarioDirectory</c>), when set,
/// overrides that one location — e.g. the <c>DevWorld</c> profile points content at <c>content/dev-world/</c>.</item>
/// <item>A relative path resolves against the <b>Hedron root</b>: the source checkout (the folder holding
/// <c>Hedron.sln</c>) when run from source, otherwise the install folder. Absolute paths and SQLite
/// URIs (<c>:memory:</c>, <c>file:…</c>) are used as given.</item>
/// <item>One exception is <b>install-relative</b>, not data: <c>Help:EngineTopicDirectory</c> (default
/// <c>content/help</c>), the committed engine help layer. It resolves against the Hedron root and does not
/// move with <c>Paths:DataDirectory</c>. The world help layer is always <c>&lt;World:ContentDirectory&gt;/help</c>.</item>
/// </list>
/// </summary>
public static class DataPaths
{
    public const string SolutionFileName = "Hedron.sln";
    public const string DefaultDataDirectory = "data";

    /// <summary>
    /// The Hedron root: the nearest ancestor of <paramref name="appBaseDirectory"/> holding
    /// <see cref="SolutionFileName"/>, else <paramref name="appBaseDirectory"/> itself (a published install).
    /// </summary>
    public static string FindRoot(string appBaseDirectory)
    {
        for (var dir = new DirectoryInfo(appBaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
                return dir.FullName;
        }
        return Path.GetFullPath(appBaseDirectory);
    }

    /// <summary>Resolves <paramref name="path"/> against <paramref name="root"/> unless it is absolute or a SQLite URI.</summary>
    public static string Resolve(string root, string path)
    {
        if (path == ":memory:" || path.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(root, path));
    }

    /// <summary>
    /// Applies the rule to every path-bearing options type. Runs after the section binding, so a
    /// per-location key that is present wins; an absent one is derived from the data directory.
    /// </summary>
    public static IServiceCollection AddDataPaths(
        this IServiceCollection services, IConfiguration configuration, string appBaseDirectory)
    {
        var root = FindRoot(appBaseDirectory);

        // Read lazily (when options are first resolved), so sources a host or test adds after
        // composition — e.g. WebApplicationFactory overrides — still apply.
        string Location(string key, string underData)
        {
            var explicitPath = configuration[key];
            if (!string.IsNullOrWhiteSpace(explicitPath))
                return Resolve(root, explicitPath);

            var configuredData = configuration["Paths:DataDirectory"];
            var dataDirectory = Resolve(root, string.IsNullOrWhiteSpace(configuredData) ? DefaultDataDirectory : configuredData);
            return Path.Combine(dataDirectory, underData);
        }

        services.PostConfigure<WorldOptions>(o => o.ContentDirectory = Location("World:ContentDirectory", "content"));
        // The one install-relative location: the engine help layer is committed, engine-owned
        // content shipped with the binaries, so it resolves against the Hedron root and never moves
        // with Paths:DataDirectory. The world help layer always sits under the world's content.
        services.PostConfigure<HelpOptions>(o =>
        {
            var engine = configuration["Help:EngineTopicDirectory"];
            o.EngineTopicDirectory = Resolve(root, string.IsNullOrWhiteSpace(engine) ? HelpOptions.DefaultEngineTopicDirectory : engine);
            o.WorldTopicDirectory = Path.Combine(Location("World:ContentDirectory", "content"), HelpTopicLayerReader.WorldSubdirectory);
        });
        services.PostConfigure<PersistenceOptions>(o => o.DatabasePath = Location("Persistence:DatabasePath", "hedron.db"));
        services.PostConfigure<BalanceOptions>(o => o.StandardsPath = Location("Balance:StandardsPath", Path.Combine("balance", "standards.yaml")));
        services.PostConfigure<SimulationOptions>(o =>
        {
            o.ReportDirectory = Location("Simulation:ReportDirectory", Path.Combine("sim", "reports"));
            o.ScenarioDirectory = Location("Simulation:ScenarioDirectory", Path.Combine("sim", "scenarios"));
        });
        return services;
    }
}
