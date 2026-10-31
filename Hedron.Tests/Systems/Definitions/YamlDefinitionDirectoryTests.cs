using System;
using System.IO;
using System.Threading.Tasks;
using Hedron.Core.Systems.Definitions;
using Xunit;

namespace Hedron.Tests.Systems.Definitions
{
    /// <summary>
    /// T2 — system-unit (temp dir) tests for <see cref="YamlDefinitionDirectory{TDto}"/>: listing,
    /// reading, sanitized atomic writes, and the root-confined <c>Delete</c>.
    /// </summary>
    public sealed class YamlDefinitionDirectoryTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hedron-yamldir-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }

        public sealed class SampleDto
        {
            public string? Name { get; set; }
        }

        private YamlDefinitionDirectory<SampleDto> NewDirectory() => new(_root);

        private void WriteFile(string fileName, string body)
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, fileName), body);
        }

        [Fact]
        public void Missing_directory_lists_nothing()
        {
            Assert.Empty(NewDirectory().List(d => d.Name));
        }

        [Fact]
        public void List_is_ordinal_uses_the_selector_and_keeps_unparseable_files()
        {
            WriteFile("b.yaml", "name: Bee\n");
            WriteFile("a.yaml", "name: [unclosed\n  : : :");
            WriteFile("c.yaml", "other: 1\n");
            WriteFile("ignored.txt", "name: no\n");

            var entries = NewDirectory().List(d => d.Name);

            Assert.Collection(entries,
                a => { Assert.Equal("a.yaml", a.FileName); Assert.Equal("a", a.Name); Assert.NotNull(a.Error); },
                b => { Assert.Equal("Bee", b.Name); Assert.Null(b.Error); },
                c => { Assert.Equal("c", c.Name); Assert.Null(c.Error); });
        }

        [Fact]
        public void Read_returns_the_dto()
        {
            WriteFile("x.yaml", "name: Ex\n");

            var result = NewDirectory().Read(Path.Combine(_root, "x.yaml"));

            Assert.True(result.Exists);
            Assert.Equal("Ex", result.Dto!.Name);
        }

        [Fact]
        public async Task WriteAsync_sanitizes_the_name_and_upserts()
        {
            var dir = NewDirectory();

            var path = await dir.WriteAsync("my topic/one", new SampleDto { Name = "first" });
            var again = await dir.WriteAsync("my topic/one", new SampleDto { Name = "second" });

            Assert.Equal(path, again);
            Assert.Equal("my-topic-one.yaml", Path.GetFileName(path));
            Assert.Equal("second", dir.Read(path).Dto!.Name);
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public async Task Multi_line_strings_are_written_as_literal_blocks_and_round_trip()
        {
            var dir = NewDirectory();

            var path = await dir.WriteAsync("doc", new SampleDto { Name = "line one\nline two\n" });

            Assert.Contains("name: |", File.ReadAllText(path));
            Assert.Equal("line one\nline two\n", dir.Read(path).Dto!.Name);
        }

        [Fact]
        public async Task WriteAsync_uses_the_fallback_for_a_blank_name()
        {
            var path = await NewDirectory().WriteAsync(" ", new SampleDto(), fallbackName: "scenario");

            Assert.Equal("scenario.yaml", Path.GetFileName(path));
        }

        [Fact]
        public void Delete_removes_a_file_and_reports_absence()
        {
            WriteFile("gone.yaml", "name: x\n");
            var dir = NewDirectory();

            Assert.True(dir.Delete(Path.Combine(_root, "gone.yaml")));
            Assert.False(File.Exists(Path.Combine(_root, "gone.yaml")));
            Assert.False(dir.Delete(Path.Combine(_root, "gone.yaml")));
        }

        [Fact]
        public void Delete_refuses_a_path_outside_the_directory()
        {
            var outside = Path.Combine(Path.GetTempPath(), "hedron-yamldir-outside-" + Guid.NewGuid().ToString("N") + ".yaml");
            File.WriteAllText(outside, "name: keep\n");
            try
            {
                var dir = NewDirectory();

                Assert.Throws<ArgumentException>(() => dir.Delete(outside));
                Assert.Throws<ArgumentException>(() => dir.Delete(Path.Combine(_root, "sub", "..", "..", Path.GetFileName(outside))));
                Assert.True(File.Exists(outside));
            }
            finally
            {
                File.Delete(outside);
            }
        }

        [Fact]
        public void Directory_exposes_the_root()
        {
            Assert.Equal(_root, NewDirectory().Directory);
        }
    }
}
