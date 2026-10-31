using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Systems.Definitions
{
    /// <summary>
    /// A directory of one-YAML-file-per-definition (sim scenarios, help topics). Lists, reads, writes
    /// and deletes <c>*.yaml</c> files under one root; each family keeps its own mapping and
    /// validation. Stateless and safe from any thread (INV-31); writes are atomic via
    /// <see cref="AtomicFileWrite"/>.
    /// </summary>
    public sealed class YamlDefinitionDirectory<TDto> where TDto : class
    {
        private readonly YamlCodecOptions _options;

        public YamlDefinitionDirectory(string directory, YamlCodecOptions? options = null)
        {
            Directory = directory;
            _options = options ?? YamlCodecOptions.Default;
        }

        public string Directory { get; }

        /// <summary>
        /// Every <c>*.yaml</c> file in ordinal path order. A file that fails to parse is listed with
        /// its <see cref="YamlFileEntry.Error"/> and a file-name-derived name — never thrown.
        /// A missing directory lists nothing.
        /// </summary>
        public IReadOnlyList<YamlFileEntry> List(Func<TDto, string?> nameOf)
        {
            if (!System.IO.Directory.Exists(Directory))
                return Array.Empty<YamlFileEntry>();

            var entries = new List<YamlFileEntry>();
            foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.yaml").OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var read = YamlCodec.Read<TDto>(path);
                if (read.Dto is not null)
                {
                    var selected = nameOf(read.Dto);
                    if (!string.IsNullOrWhiteSpace(selected))
                        name = selected;
                }

                entries.Add(new YamlFileEntry(path, Path.GetFileName(path), name, read.Error));
            }

            return entries;
        }

        /// <summary>Never throws — see <see cref="YamlReadResult{TDto}"/>.</summary>
        public YamlReadResult<TDto> Read(string path) => YamlCodec.Read<TDto>(path);

        /// <summary>
        /// Writes <paramref name="dto"/> to <c>&lt;Directory&gt;/&lt;sanitized name&gt;.yaml</c> (upsert) and
        /// returns the full path. Invalid file-name characters and spaces become <c>-</c>; a blank
        /// name becomes <paramref name="fallbackName"/>.
        /// </summary>
        public async Task<string> WriteAsync(string name, TDto dto, CancellationToken ct = default, string fallbackName = "definition")
        {
            System.IO.Directory.CreateDirectory(Directory);

            var path = Path.Combine(Directory, Sanitize(name, fallbackName) + ".yaml");
            var body = YamlCodec.Serializer(_options).Serialize(dto);

            await AtomicFileWrite.ReplaceAsync(path, body, ct).ConfigureAwait(false);
            return path;
        }

        /// <summary>
        /// Deletes one file inside <see cref="Directory"/>. Returns <c>false</c> when it is absent.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="path"/> is not inside <see cref="Directory"/>.</exception>
        public bool Delete(string path)
        {
            var root = Path.GetFullPath(Directory);
            var full = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(full);
            if (parent is null || !string.Equals(
                    Path.TrimEndingDirectorySeparator(parent),
                    Path.TrimEndingDirectorySeparator(root),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new ArgumentException($"'{path}' is not a file in '{Directory}'.", nameof(path));
            }

            if (!File.Exists(full))
                return false;

            File.Delete(full);
            return true;
        }

        public static string Sanitize(string name, string fallbackName = "definition")
        {
            if (string.IsNullOrWhiteSpace(name))
                return fallbackName;

            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '-' : c).ToArray());
        }
    }
}
