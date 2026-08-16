using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Systems
{
    /// <summary>
    /// Tier 1 — <see cref="AtomicFileWrite"/>, the single write-temp-then-replace publish every YAML
    /// and JSON file writer in the engine uses (authoring-api-surface WP2, extracted from seven
    /// copies). Its decisions are: the replace is atomic, a reader holding the destination does not
    /// break it, the retry set and attempt bound, and that the final attempt's failure propagates
    /// rather than being retried into silence.
    /// </summary>
    public sealed class AtomicFileWriteTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

        private string NewTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hedron-atomic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);
            return dir;
        }

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best-effort temp cleanup */ }
            }
        }

        [Fact]
        public async Task Writes_a_new_file()
        {
            var path = Path.Combine(NewTempDir(), "new.yaml");

            await AtomicFileWrite.ReplaceAsync(path, "hello");

            Assert.Equal("hello", File.ReadAllText(path));
        }

        [Fact]
        public async Task Replaces_an_existing_file()
        {
            var path = Path.Combine(NewTempDir(), "existing.yaml");
            File.WriteAllText(path, "old");

            await AtomicFileWrite.ReplaceAsync(path, "new");

            Assert.Equal("new", File.ReadAllText(path));
        }

        [Fact]
        public async Task Leaves_no_temp_file_behind()
        {
            var dir = NewTempDir();
            var path = Path.Combine(dir, "clean.yaml");

            await AtomicFileWrite.ReplaceAsync(path, "body");

            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }

        [Fact]
        public async Task Succeeds_while_a_reader_holds_the_destination_share_delete()
        {
            // The interaction the whole design turns on: catalog reads are lock-free and concurrent
            // with writes, so ContentFileReader opens with FileShare.ReadWrite | Delete precisely so
            // this replace can still proceed. If this fails, concurrent reads break concurrent
            // writes and the INV-31 posture is a fiction.
            var path = Path.Combine(NewTempDir(), "held.yaml");
            File.WriteAllText(path, "old");

            using (var reader = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                await AtomicFileWrite.ReplaceAsync(path, "new");
            }

            Assert.Equal("new", File.ReadAllText(path));
        }

        [Fact]
        public async Task A_reader_observes_either_the_whole_old_file_or_the_whole_new_one()
        {
            var path = Path.Combine(NewTempDir(), "torn.yaml");
            var oldBody = new string('a', 200_000);
            var newBody = new string('b', 200_000);
            File.WriteAllText(path, oldBody);

            var stop = false;
            var reader = Task.Run(() =>
            {
                while (!Volatile.Read(ref stop))
                {
                    string seen;
                    try
                    {
                        using var stream = new FileStream(
                            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var text = new StreamReader(stream);
                        seen = text.ReadToEnd();
                    }
                    catch (IOException)
                    {
                        continue; // mid-replace open failure is not a torn read
                    }

                    // Never a truncated body and never a mixture of the two.
                    Assert.True(
                        seen == oldBody || seen == newBody,
                        $"observed a torn file: {seen.Length} chars, starts with '{(seen.Length > 0 ? seen[0] : ' ')}'");
                }
            });

            for (var i = 0; i < 30; i++)
                await AtomicFileWrite.ReplaceAsync(path, i % 2 == 0 ? newBody : oldBody);

            Volatile.Write(ref stop, true);
            await reader;
        }

        [Fact]
        public async Task An_exclusively_locked_destination_throws_after_the_attempt_bound()
        {
            // FileShare.None denies the delete the replace needs, so every attempt fails. The
            // contract is that the last one propagates — a genuine permission failure must surface,
            // not be retried into silence — and that it does so promptly rather than hanging.
            var path = Path.Combine(NewTempDir(), "locked.yaml");
            File.WriteAllText(path, "old");

            // Scoped so the lock is released before the readback below — FileShare.None locks out
            // this test too.
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var attempt = AtomicFileWrite.ReplaceAsync(path, "new");
                var finished = await Task.WhenAny(attempt, Task.Delay(TimeSpan.FromSeconds(10)));

                Assert.True(finished == attempt, "ReplaceAsync hung instead of exhausting its retry bound.");
                await Assert.ThrowsAnyAsync<Exception>(() => attempt);
            }

            Assert.Equal("old", File.ReadAllText(path));
        }
    }
}
