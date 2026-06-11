using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Server
{
    /// <summary>
    /// Headless <c>generate</c> run-mode: a no-chain Initiator (INV-10) that composes the engine's
    /// DI (no gameplay hosted services — no telnet/heartbeat/world-spawn), loads a generation
    /// profile, runs one <see cref="IContentGenerationSystem.GenerateAsync"/>, validates each
    /// emitted definition via <see cref="IContentValidator"/> (Resolved Decision 4 — in-memory,
    /// no live entities), prints a summary, and exits 0 (clean) / non-zero (validation or write
    /// failure).
    /// </summary>
    /// <remarks>
    /// This is the host's process-level shell around the Core generation logic (INV-8): it owns arg
    /// parsing, profile loading, validation policy, and the exit code; the system owns the
    /// generation. It publishes nothing and starts no listener or heartbeat.
    /// </remarks>
    public static class GenerationRunMode
    {
        /// <summary>Recognizes the run-mode token as the first CLI argument.</summary>
        public static bool Matches(string[] args) => args.Length > 0 && args[0] == "generate";

        public static async Task<int> RunAsync(string[] args, IConfiguration configuration)
        {
            string? profilePath = null;
            int? seedOverride = null;

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--profile" when i + 1 < args.Length:
                        profilePath = args[++i];
                        break;
                    case "--seed" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], out var seed))
                        {
                            Console.Error.WriteLine($"generate: --seed must be an integer, got '{args[i]}'.");
                            return 2;
                        }
                        seedOverride = seed;
                        break;
                    default:
                        Console.Error.WriteLine($"generate: unrecognized argument '{args[i]}'.");
                        PrintUsage();
                        return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(profilePath))
            {
                Console.Error.WriteLine("generate: --profile <path> is required.");
                PrintUsage();
                return 2;
            }

            GenerationProfile profile;
            try
            {
                profile = LoadProfile(profilePath, seedOverride);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"generate: failed to load profile '{profilePath}': {ex.Message}");
                return 2;
            }

            // Compose DI only — no gameplay hosted services (no telnet/heartbeat/world spawn).
            // The Ability/Effect/Aspect/Stat definition registries self-populate at construction,
            // so the validator's cross-ref checks work with no bootstrap (Resolved Decision 4).
            var services = new ServiceCollection();
            services.Register(configuration);
            await using var provider = services.BuildServiceProvider();

            var generator = provider.GetRequiredService<IContentGenerationSystem>();
            var validator = provider.GetRequiredService<IContentValidator>();
            var contentDirectory = provider.GetRequiredService<IOptions<WorldOptions>>().Value.ContentDirectory;

            GenerationResult result;
            try
            {
                result = await generator.GenerateAsync(profile);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"generate: generation failed: {ex.Message}");
                return 1;
            }

            // Validate each emitted definition in-memory (single-definition mode, no live entities).
            var validationErrors = ValidateEmitted(contentDirectory, validator);

            PrintSummary(profile, result, validationErrors);

            return validationErrors.Count == 0 ? 0 : 1;
        }

        private static GenerationProfile LoadProfile(string path, int? seedOverride)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"profile file not found at '{path}'.");

            var body = File.ReadAllText(path);
            var yaml = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var dto = yaml.Deserialize<ProfileDto>(body)
                ?? throw new InvalidOperationException("profile YAML is empty.");

            var aspectMix = new List<AspectMixEntry>();
            if (dto.AspectMix is { Count: > 0 })
            {
                foreach (var entry in dto.AspectMix)
                {
                    if (string.IsNullOrWhiteSpace(entry.Aspect))
                        continue;
                    if (!Enum.TryParse<AspectId>(entry.Aspect, ignoreCase: true, out var aspectId))
                        throw new InvalidOperationException($"unknown aspect '{entry.Aspect}' in aspectMix.");
                    aspectMix.Add(new AspectMixEntry(aspectId, entry.Weight));
                }
            }

            var scaling = ScalingCurve.Linear;
            if (!string.IsNullOrWhiteSpace(dto.Scaling) &&
                !Enum.TryParse(dto.Scaling, ignoreCase: true, out scaling))
                throw new InvalidOperationException($"unknown scaling curve '{dto.Scaling}'.");

            return new GenerationProfile
            {
                Seed = seedOverride ?? dto.Seed,
                AreaCount = dto.AreaCount,
                RoomsPerArea = (dto.RoomsPerArea?.Min ?? 1, dto.RoomsPerArea?.Max ?? 1),
                LevelRange = (dto.LevelRange?.Min ?? 1, dto.LevelRange?.Max ?? 1),
                MobDensity = dto.MobDensity,
                ItemDensity = dto.ItemDensity,
                AspectMix = aspectMix,
                Scaling = scaling,
                BlueprintPrefix = string.IsNullOrWhiteSpace(dto.BlueprintPrefix) ? "gen." : dto.BlueprintPrefix,
            };
        }

        /// <summary>
        /// Re-reads every emitted definition file, deserializes it through its existing deserializer,
        /// and runs the single-definition validator over it. Accumulates one error string per
        /// offending file.
        /// </summary>
        private static List<string> ValidateEmitted(string contentDirectory, IContentValidator validator)
        {
            var errors = new List<string>();

            ValidateKind<AreaTemplate>(
                Path.Combine(contentDirectory, "areas"),
                body => new AreaTemplateDeserializer(NullLog<AreaTemplateDeserializer>()).Deserialize(body),
                validator, errors);
            ValidateKind<RoomTemplate>(
                Path.Combine(contentDirectory, "rooms"),
                body => new RoomTemplateDeserializer(NullLog<RoomTemplateDeserializer>()).Deserialize(body),
                validator, errors);
            ValidateKind<ItemTemplate>(
                Path.Combine(contentDirectory, "items"),
                body => new Hedron.Core.Modules.Items.ItemTemplateDeserializer(
                    NullLog<Hedron.Core.Modules.Items.ItemTemplateDeserializer>()).Deserialize(body),
                validator, errors);
            ValidateKind<MobTemplate>(
                Path.Combine(contentDirectory, "mobs"),
                body => new Hedron.Core.Modules.Mobs.MobTemplateDeserializer(
                    NullLog<Hedron.Core.Modules.Mobs.MobTemplateDeserializer>()).Deserialize(body),
                validator, errors);

            return errors;
        }

        private static void ValidateKind<T>(
            string directory,
            Func<string, IEntityTemplate> deserialize,
            IContentValidator validator,
            List<string> errors)
            where T : IEntityTemplate
        {
            if (!Directory.Exists(directory)) return;

            foreach (var file in Directory.EnumerateFiles(directory, "*.yaml").OrderBy(f => f, StringComparer.Ordinal))
            {
                IEntityTemplate template;
                try
                {
                    template = deserialize(File.ReadAllText(file));
                }
                catch (Exception ex)
                {
                    errors.Add($"{file}: failed to deserialize — {ex.Message}");
                    continue;
                }

                var report = validator.Validate(template);
                if (!report.IsValid)
                    foreach (var error in report.Errors)
                        errors.Add($"{file}: {error}");
            }
        }

        private static void PrintSummary(
            GenerationProfile profile, GenerationResult result, IReadOnlyList<string> validationErrors)
        {
            Console.WriteLine("Content generation summary");
            Console.WriteLine($"  seed:   {profile.Seed}");
            Console.WriteLine($"  areas:  {result.AreasWritten}");
            Console.WriteLine($"  rooms:  {result.RoomsWritten}");
            Console.WriteLine($"  mobs:   {result.MobsWritten}");
            Console.WriteLine($"  items:  {result.ItemsWritten}");

            const int sample = 10;
            var shown = Math.Min(sample, result.BlueprintIds.Count);
            if (shown > 0)
            {
                Console.WriteLine($"  blueprint ids (first {shown} of {result.BlueprintIds.Count}):");
                for (var i = 0; i < shown; i++)
                    Console.WriteLine($"    {result.BlueprintIds[i]}");
            }

            if (validationErrors.Count == 0)
            {
                Console.WriteLine("  validation: OK");
            }
            else
            {
                Console.WriteLine($"  validation: {validationErrors.Count} error(s):");
                foreach (var error in validationErrors)
                    Console.WriteLine($"    • {error}");
            }
        }

        private static void PrintUsage() =>
            Console.Error.WriteLine("usage: dotnet run --project Server -- generate --profile <path> [--seed N]");

        private static Microsoft.Extensions.Logging.ILogger<T> NullLog<T>() =>
            Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;

        // ── Profile YAML DTO (camelCase, same convention as content files) ────────────

        private sealed class ProfileDto
        {
            public int Seed { get; set; }
            public int AreaCount { get; set; } = 1;
            public RangeDto? RoomsPerArea { get; set; }
            public RangeDto? LevelRange { get; set; }
            public double MobDensity { get; set; }
            public double ItemDensity { get; set; }
            public List<AspectMixDto>? AspectMix { get; set; }
            public string? Scaling { get; set; }
            public string? BlueprintPrefix { get; set; }
        }

        private sealed class RangeDto
        {
            public int Min { get; set; }
            public int Max { get; set; }
        }

        private sealed class AspectMixDto
        {
            public string? Aspect { get; set; }
            public int Weight { get; set; }
        }
    }
}
