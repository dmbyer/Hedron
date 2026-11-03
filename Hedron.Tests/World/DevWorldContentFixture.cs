using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Account;
using Hedron.Core.Modules.BalanceInspection;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Shared xUnit class fixture over the committed <c>content/dev-world/</c>: copies it once per
    /// test class into a fresh temp directory (so the loader can never write into the committed
    /// tree), boots the copy through the real <see cref="WorldContentLoader"/>, and exposes the
    /// loaded <see cref="TemplateRegistry"/>, the <see cref="WorldConfiguration"/>, a
    /// <see cref="RegistryPlacementSource"/> and the balance standards dev-world is graded against.
    /// Consumers must treat the registry as read-only — one boot serves every fact in the class.
    /// </summary>
    public sealed class DevWorldContentFixture : IAsyncLifetime
    {
        /// <summary>The committed DevWorld profile the standards path is read from (one source with the hosts).</summary>
        private const string ProfileRelativePath = "Server/appsettings.DevWorld.json";
        private const string ContentRelativePath = "content/dev-world/";

        public string RepoRoot { get; } = FindRepoRoot();
        public string ContentDirectory { get; } = Path.Combine(Path.GetTempPath(), $"hedron-devworld-{Guid.NewGuid():N}");
        public TemplateRegistry Registry { get; private set; } = null!;
        public WorldConfiguration Config { get; private set; } = null!;
        public RegistryPlacementSource Placement { get; private set; } = null!;

        /// <summary>The standards the DevWorld host loads (compiled defaults while the pinned file is absent).</summary>
        public BalanceStandardsDocument Standards { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            CopyDirectory(Path.Combine(RepoRoot, "content", "dev-world"), ContentDirectory);

            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var config = new WorldConfiguration();
            // Deliberately a config id that exists nowhere: the start must come from the manifest.
            var options = Options.Create(new WorldOptions
            {
                ContentDirectory = ContentDirectory,
                StartingRoomBlueprintId = "room.not-in-dev-world",
            });
            var serializer = new YamlContentSerializer(new ITemplateDeserializer[]
            {
                new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance),
                new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance),
                new ItemTemplateDeserializer(NullLogger<ItemTemplateDeserializer>.Instance),
                new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance),
            });
            var loader = new WorldContentLoader(
                ecs, registry, serializer, new RoomContentWriter(options),
                new WorldManifestStore(options), config, options, NullLogger<WorldContentLoader>.Instance);
            await loader.LoadAndSpawnAsync();

            Registry = registry;
            Config = config;
            Placement = new RegistryPlacementSource(registry);
            Standards = LoadStandards();
        }

        public Task DisposeAsync()
        {
            if (Directory.Exists(ContentDirectory)) Directory.Delete(ContentDirectory, recursive: true);
            return Task.CompletedTask;
        }

        /// <summary>The band-drift audit as the DevWorld host composes it, over the loaded registry.</summary>
        public BalanceAuditSystem Audit() => new(
            Registry,
            new PowerBudgetSystem(Standards.Tunables),
            new ItemPowerProjectionSystem(),
            new MobPowerProjectionSystem(),
            Standards.Tunables,
            Standards.BandDriftTolerance);

        /// <summary>The committed sim scenario folder inside the temp copy.</summary>
        public string ScenarioDirectory => Path.Combine(ContentDirectory, "sim");

        // The profile's Balance:StandardsPath (relative to the Hedron root) points inside the
        // committed dev-world tree; rebase it onto the temp copy so the guard reads the same file
        // the host does — or, while it is absent, the same compiled defaults.
        private BalanceStandardsDocument LoadStandards()
        {
            using var profile = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, ProfileRelativePath)));
            var pinned = profile.RootElement.GetProperty("Balance").GetProperty("StandardsPath").GetString()
                ?? throw new InvalidOperationException("DevWorld profile lacks Balance:StandardsPath.");
            if (!pinned.StartsWith(ContentRelativePath, StringComparison.Ordinal))
                throw new InvalidOperationException($"Balance:StandardsPath '{pinned}' is not under {ContentRelativePath}.");

            var rebased = Path.Combine(ContentDirectory, pinned[ContentRelativePath.Length..]);
            var store = new BalanceStandardsStore(
                Options.Create(new BalanceOptions { StandardsPath = rebased }),
                Options.Create(new CharacterDefaultsOptions()),
                new AbilityRegistry());
            return store.Load().Document;
        }

        internal static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hedron.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Hedron.sln not found above the test output.");
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
    }
}
