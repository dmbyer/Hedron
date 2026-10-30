using Microsoft.Extensions.Configuration;

namespace Hedron.Server;

/// <summary>
/// The one configuration layering for the Server process — the telnet host and the headless run
/// modes (<c>generate</c>, <c>simulate</c>): <c>appsettings.json</c>, then
/// <c>appsettings.{environment}.json</c> when an environment is named, then <c>HEDRON_</c> environment
/// variables. The JSON is read from the build output (<c>AppContext.BaseDirectory</c>), so it applies
/// whatever the working directory; relative <b>data</b> paths inside it stay working-directory-relative.
/// </summary>
public static class RunModeConfiguration
{
    /// <summary>
    /// The environment name the run modes use: <c>DOTNET_ENVIRONMENT</c> only (the run modes do not
    /// parse the telnet host's <c>--environment</c> argument).
    /// </summary>
    public static string? CurrentEnvironmentName() =>
        System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

    /// <param name="basePath">Directory holding the JSON files (callers pass <c>AppContext.BaseDirectory</c>).</param>
    /// <param name="environmentName">Environment whose override file to layer; null/blank loads the base file only.</param>
    public static IConfiguration Build(string basePath, string? environmentName) =>
        AddJsonLayers(new ConfigurationBuilder(), basePath, environmentName)
            .AddEnvironmentVariables("HEDRON_")
            .Build();

    /// <summary>
    /// Replaces the telnet host's app-configuration sources: build-output JSON (base + environment),
    /// then the command line, then <c>HEDRON_</c> variables. Unprefixed environment variables are
    /// deliberately not a source: <c>HEDRON_</c> is the one environment-variable mechanism.
    /// </summary>
    /// <remarks>
    /// Clearing also drops the generic host's chained host-configuration source, so host-level keys
    /// (<c>environment</c>, <c>contentRoot</c>) are not readable from app configuration. Nothing reads
    /// them there; use <c>IHostEnvironment</c> instead.
    /// </remarks>
    public static void ConfigureHost(
        IConfigurationBuilder builder, string basePath, string? environmentName, string[] args)
    {
        builder.Sources.Clear();
        AddJsonLayers(builder, basePath, environmentName);
        builder.AddCommandLine(args);
        builder.AddEnvironmentVariables("HEDRON_");
    }

    /// <summary>Adds the base and environment JSON files rooted at <paramref name="basePath"/>.</summary>
    public static IConfigurationBuilder AddJsonLayers(
        IConfigurationBuilder builder, string basePath, string? environmentName)
    {
        builder.SetBasePath(basePath).AddJsonFile("appsettings.json", optional: true);
        if (!string.IsNullOrWhiteSpace(environmentName))
            builder.AddJsonFile($"appsettings.{environmentName}.json", optional: true);
        return builder;
    }
}
