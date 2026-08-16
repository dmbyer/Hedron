using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// The one write-temp-then-replace publish every YAML/JSON file writer in the engine uses —
    /// content writers, the balance-standards store, and the simulation scenario/report writers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why atomic.</strong> Catalog reads are lock-free and concurrent with writes (INV-31),
    /// so a plain in-place rewrite would let a reader observe a truncated file. Writing a sibling
    /// <c>.tmp</c> and replacing means a reader sees either the whole old file or the whole new one.
    /// </para>
    /// <para>
    /// <strong>Why <see cref="File.Replace(string,string,string)"/> and not
    /// <c>File.Move(overwrite: true)</c>.</strong> Measured on Windows, with a reader holding the
    /// destination open:
    /// </para>
    /// <list type="table">
    ///   <item><term><c>Move(overwrite)</c>, reader <c>FileShare.Read</c></term><description>fails — <c>UnauthorizedAccessException</c></description></item>
    ///   <item><term><c>Move(overwrite)</c>, reader <c>ReadWrite|Delete</c></term><description>fails — <c>UnauthorizedAccessException</c></description></item>
    ///   <item><term><c>Replace()</c>, reader <c>FileShare.Read</c></term><description>fails — <c>IOException</c></description></item>
    ///   <item><term><c>Replace()</c>, reader <c>ReadWrite|Delete</c></term><description><strong>succeeds</strong></description></item>
    /// </list>
    /// <para>
    /// So the two halves are jointly necessary: <c>Replace</c> here, and
    /// <c>FileShare.ReadWrite | FileShare.Delete</c> in <c>ContentFileReader</c>. <c>Move</c> cannot
    /// rename over a delete-pending destination no matter how the reader opened it, which is why
    /// relaxing the share flags alone did nothing. <c>Move</c> is still used for the
    /// <em>create</em> case, where there is no destination to replace.
    /// </para>
    /// <para>
    /// <strong>Why the retry survives anyway.</strong> A virus scanner or indexer can hold the
    /// destination with flags nothing here controls, and the create case races another writer. The
    /// backoff is short and bounded, and the final attempt's exception propagates — a genuine
    /// permission failure must still surface, not be retried into silence. It is a backstop, not the
    /// mechanism: the concurrent-reader case is handled correctly above, not merely retried past.
    /// </para>
    /// <para>
    /// This makes one file's publish atomic. It does <em>not</em> make a multi-file cascade
    /// transactional — that remains the catalog's recorded, backlog-tracked debt.
    /// </para>
    /// </remarks>
    public static class AtomicFileWrite
    {
        private const int MaxAttempts = 5;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);

        /// <summary>
        /// Writes <paramref name="body"/> to a sibling temp file, then atomically replaces
        /// <paramref name="path"/> with it.
        /// </summary>
        /// <remarks>
        /// Named <c>ReplaceAsync</c>, not <c>PublishAsync</c>: this type lives under
        /// <c>Core/Systems/</c>, and the INV-5 review sweep greps for <c>PublishAsync</c> under
        /// <c>Systems/</c> to catch a system touching the event bus. Seven writers calling a
        /// file helper by that name would make every future sweep triage eight false positives.
        /// </remarks>
        public static async Task ReplaceAsync(string path, string body, CancellationToken ct = default)
        {
            var tmpPath = path + ".tmp";
            await File.WriteAllTextAsync(tmpPath, body, ct).ConfigureAwait(false);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        // Replace, not Move: the only form that survives a concurrent reader (see
                        // the measured table in the class remarks). No backup file is kept.
                        File.Replace(tmpPath, path, destinationBackupFileName: null);
                    }
                    else
                    {
                        // Nothing to replace. A racing writer that creates the file between the
                        // probe and here makes this throw, and the retry re-enters on the Replace
                        // branch — which is why the existence check is inside the loop.
                        File.Move(tmpPath, path);
                    }
                    return;
                }
                catch (Exception ex) when (attempt < MaxAttempts && IsTransientSharingFailure(ex))
                {
                    await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
                }
            }
        }

        private static bool IsTransientSharingFailure(Exception ex) =>
            ex is UnauthorizedAccessException || ex is IOException;
    }
}
