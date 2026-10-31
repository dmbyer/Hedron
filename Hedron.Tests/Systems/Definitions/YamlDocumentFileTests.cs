using System;
using System.IO;
using System.Threading.Tasks;
using Hedron.Core.Systems.Definitions;
using Xunit;

namespace Hedron.Tests.Systems.Definitions
{
    /// <summary>
    /// T1 — system-unit (temp dir) tests for <see cref="YamlDocumentFile{TDto}"/>: missing,
    /// malformed, round-trip and null-omission behavior of the shared single-document store.
    /// </summary>
    public sealed class YamlDocumentFileTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "hedron-yamldoc-" + Guid.NewGuid().ToString("N"));

        public YamlDocumentFileTests() => Directory.CreateDirectory(_tempDir);

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }

        public sealed class SampleDto
        {
            public string? Name { get; set; }
            public int? Count { get; set; }
        }

        private string FilePath => Path.Combine(_tempDir, "nested", "doc.yaml");

        [Fact]
        public void Missing_file_reads_as_not_existing_without_error()
        {
            var result = new YamlDocumentFile<SampleDto>(FilePath).Read();

            Assert.False(result.Exists);
            Assert.Null(result.Dto);
            Assert.Null(result.Error);
            Assert.Null(result.Fault);
        }

        [Fact]
        public void Malformed_yaml_returns_error_and_fault_without_throwing()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, "name: [unclosed\n  : : :");

            var result = new YamlDocumentFile<SampleDto>(FilePath).Read();

            Assert.True(result.Exists);
            Assert.Null(result.Dto);
            Assert.NotNull(result.Error);
            Assert.NotNull(result.Fault);
            Assert.Equal(result.Fault!.Message, result.Error);
        }

        [Fact]
        public void Empty_file_exists_with_null_dto_and_no_error()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, "");

            var result = new YamlDocumentFile<SampleDto>(FilePath).Read();

            Assert.True(result.Exists);
            Assert.Null(result.Dto);
            Assert.Null(result.Error);
        }

        [Fact]
        public async Task Write_creates_the_directory_and_round_trips_camel_case()
        {
            var file = new YamlDocumentFile<SampleDto>(FilePath);

            await file.WriteAsync(new SampleDto { Name = "alpha", Count = 3 });

            Assert.Contains("name: alpha", File.ReadAllText(FilePath));
            var result = file.Read();
            Assert.Equal("alpha", result.Dto!.Name);
            Assert.Equal(3, result.Dto.Count);
        }

        [Fact]
        public async Task Nulls_are_written_by_default_and_omitted_when_opted_in()
        {
            await new YamlDocumentFile<SampleDto>(FilePath).WriteAsync(new SampleDto { Name = "a" });
            Assert.Contains("count:", File.ReadAllText(FilePath));

            await new YamlDocumentFile<SampleDto>(FilePath, new YamlCodecOptions(OmitNulls: true)).WriteAsync(new SampleDto { Name = "a" });
            Assert.DoesNotContain("count:", File.ReadAllText(FilePath));
        }

        [Fact]
        public void Unknown_keys_are_ignored_on_read()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, "name: a\nextra: ignored\n");

            var result = new YamlDocumentFile<SampleDto>(FilePath).Read();

            Assert.Null(result.Error);
            Assert.Equal("a", result.Dto!.Name);
        }
    }
}
