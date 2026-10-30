using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Systems;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Default <see cref="IWorldManifestStore"/>: hand-rolled single-document YAML load/save of
    /// <c>&lt;World:ContentDirectory&gt;/world.yaml</c>. Builds its YamlDotNet (de)serializer per call
    /// (YamlDotNet is not thread-safe) and keeps no cached manifest, so it is safe from any thread
    /// (INV-31). Bounded debt: migrates onto the shared YAML definition store in the help-topics slice.
    /// </summary>
    public sealed class WorldManifestStore : IWorldManifestStore
    {
        public const string FileName = "world.yaml";

        public WorldManifestStore(IOptions<WorldOptions> options)
        {
            FilePath = Path.Combine(options.Value.ContentDirectory, FileName);
        }

        public string FilePath { get; }

        public WorldManifestReadResult Read()
        {
            if (!File.Exists(FilePath))
                return new WorldManifestReadResult(WorldManifest.Empty, Exists: false, Error: null);

            try
            {
                var body = File.ReadAllText(FilePath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                var dto = deserializer.Deserialize<ManifestDto?>(body);
                var manifest = dto is null
                    ? WorldManifest.Empty
                    : new WorldManifest(dto.SchemaVersion, string.IsNullOrWhiteSpace(dto.StartingRoomId) ? null : dto.StartingRoomId.Trim());
                return new WorldManifestReadResult(manifest, Exists: true, Error: null);
            }
            catch (Exception ex)
            {
                return new WorldManifestReadResult(WorldManifest.Empty, Exists: true, Error: ex.Message);
            }
        }

        public async Task WriteAsync(WorldManifest manifest, CancellationToken ct = default)
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build();
            var body = serializer.Serialize(new ManifestDto
            {
                SchemaVersion = manifest.SchemaVersion,
                StartingRoomId = manifest.StartingRoomId,
            });

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await AtomicFileWrite.ReplaceAsync(FilePath, body, ct).ConfigureAwait(false);
        }

        public async Task<WorldManifestUpdateResult> UpdateAsync(
            Func<WorldManifest, WorldManifest> change, CancellationToken ct = default)
        {
            var read = Read();
            if (read.Error is not null)
                return new WorldManifestUpdateResult(false, null, read.Error);

            var changed = change(read.Manifest);
            var toWrite = changed with
            {
                SchemaVersion = changed.SchemaVersion ?? WorldManifest.CurrentSchemaVersion,
            };
            await WriteAsync(toWrite, ct).ConfigureAwait(false);
            return new WorldManifestUpdateResult(true, toWrite, null);
        }

        private sealed class ManifestDto
        {
            public int? SchemaVersion { get; set; }
            public string? StartingRoomId { get; set; }
        }
    }
}
