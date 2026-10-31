using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Systems.Definitions;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Default <see cref="IWorldManifestStore"/>: single-document YAML load/save of
    /// <c>&lt;World:ContentDirectory&gt;/world.yaml</c> over the shared
    /// <see cref="YamlDocumentFile{TDto}"/> (nulls omitted on write). Keeps no cached manifest, so it
    /// is safe from any thread (INV-31).
    /// </summary>
    public sealed class WorldManifestStore : IWorldManifestStore
    {
        public const string FileName = "world.yaml";

        private readonly YamlDocumentFile<ManifestDto> _file;

        public WorldManifestStore(IOptions<WorldOptions> options)
        {
            FilePath = Path.Combine(options.Value.ContentDirectory, FileName);
            _file = new YamlDocumentFile<ManifestDto>(FilePath, new YamlCodecOptions(OmitNulls: true));
        }

        public string FilePath { get; }

        public WorldManifestReadResult Read()
        {
            var read = _file.Read();
            if (!read.Exists)
                return new WorldManifestReadResult(WorldManifest.Empty, Exists: false, Error: null);
            if (read.Error is not null)
                return new WorldManifestReadResult(WorldManifest.Empty, Exists: true, Error: read.Error);

            var dto = read.Dto;
            var manifest = dto is null
                ? WorldManifest.Empty
                : new WorldManifest(dto.SchemaVersion, string.IsNullOrWhiteSpace(dto.StartingRoomId) ? null : dto.StartingRoomId.Trim());
            return new WorldManifestReadResult(manifest, Exists: true, Error: null);
        }

        public Task WriteAsync(WorldManifest manifest, CancellationToken ct = default) =>
            _file.WriteAsync(new ManifestDto
            {
                SchemaVersion = manifest.SchemaVersion,
                StartingRoomId = manifest.StartingRoomId,
            }, ct);

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
