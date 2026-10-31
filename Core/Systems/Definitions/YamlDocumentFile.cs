using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Systems.Definitions
{
    /// <summary>
    /// A single-document YAML definition file (balance standards, the world manifest). Load/save
    /// only: each family keeps its own DTO ⇄ domain mapping, validation and warnings. Stateless and
    /// safe from any thread (INV-31); writes are atomic via <see cref="AtomicFileWrite"/>.
    /// </summary>
    public sealed class YamlDocumentFile<TDto> where TDto : class
    {
        private readonly YamlCodecOptions _options;

        public YamlDocumentFile(string path, YamlCodecOptions? options = null)
        {
            Path = path;
            _options = options ?? YamlCodecOptions.Default;
        }

        public string Path { get; }

        /// <summary>Never throws — see <see cref="YamlReadResult{TDto}"/>.</summary>
        public YamlReadResult<TDto> Read() => YamlCodec.Read<TDto>(Path);

        public async Task WriteAsync(TDto dto, CancellationToken ct = default)
        {
            var body = YamlCodec.Serializer(_options).Serialize(dto);

            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await AtomicFileWrite.ReplaceAsync(Path, body, ct).ConfigureAwait(false);
        }
    }
}
