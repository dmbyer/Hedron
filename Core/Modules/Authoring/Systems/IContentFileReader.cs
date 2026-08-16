using System;
using System.Collections.Generic;
using System.IO;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// The read half of <see cref="ContentDefinitionCatalog"/>'s filesystem access, behind a seam
    /// so a test can count directory sweeps and file reads deterministically (INV-26 — a port for
    /// an otherwise-unobservable I/O effect, not a general filesystem abstraction).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Deliberately catalog-scoped.</strong> The authoring module already has three
    /// filesystem styles: <see cref="ContentReferenceIndex"/> reads <c>System.IO</c> directly, the
    /// <c>I*ContentWriter</c> family owns writes, and this seam wraps the catalog's reads. It is an
    /// infrastructure <em>port</em>, not a domain system — it gets no <c>reference/systems.md</c>
    /// row (INV-16/INV-29) but is DI-registered so the composition-root smoke guard covers it.
    /// A later slice that needs broader filesystem indirection should widen this seam rather than
    /// hand-roll a fourth style (INV-19).
    /// </para>
    /// </remarks>
    public interface IContentFileReader
    {
        /// <summary>Whether <paramref name="path"/> exists as a directory.</summary>
        bool DirectoryExists(string path);

        /// <summary>
        /// One directory sweep: the files in <paramref name="directory"/> matching
        /// <paramref name="searchPattern"/>. Returns empty when the directory is absent.
        /// </summary>
        IReadOnlyList<string> GetFiles(string directory, string searchPattern);

        /// <summary>Whether <paramref name="path"/> exists as a file.</summary>
        bool FileExists(string path);

        /// <summary>
        /// Reads the full text of <paramref name="path"/>. Must not block a concurrent writer's
        /// atomic replace of the same file — see <see cref="ContentFileReader.ReadAllText"/>.
        /// </summary>
        string ReadAllText(string path);
    }

    /// <summary>
    /// Production <see cref="IContentFileReader"/> — a thin pass-through to <c>System.IO</c>.
    /// </summary>
    public sealed class ContentFileReader : IContentFileReader
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public IReadOnlyList<string> GetFiles(string directory, string searchPattern) =>
            Directory.Exists(directory)
                ? Directory.GetFiles(directory, searchPattern)
                : Array.Empty<string>();

        public bool FileExists(string path) => File.Exists(path);

        /// <summary>
        /// Reads with <c>FileShare.ReadWrite | FileShare.Delete</c> rather than via
        /// <c>File.ReadAllText</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <c>I*ContentWriter</c> family publishes through
        /// <see cref="Hedron.Core.Systems.AtomicFileWrite"/>: write a <c>.tmp</c> file, then
        /// <c>File.Replace</c> the destination with it. Catalog reads are lock-free and concurrent
        /// with those writes by design (INV-31), and on Windows <c>File.Replace</c> only succeeds
        /// while a reader holds the destination if that reader permitted deletion — which
        /// <c>File.ReadAllText</c>'s default <c>FileShare.Read</c> does not.
        /// </para>
        /// <para>
        /// <strong>These two choices are jointly necessary, and neither works alone</strong>
        /// (measured — see the table in <c>AtomicFileWrite</c>'s remarks): relaxing the share flags
        /// while the writer used <c>File.Move(overwrite: true)</c> changed nothing, because
        /// <c>Move</c> cannot rename over a delete-pending destination however it was opened. Do not
        /// tighten these flags back to <c>FileShare.Read</c>, and do not switch the writer back to
        /// <c>Move</c>; either one on its own re-breaks concurrent reads.
        /// </para>
        /// <para>
        /// Readers still never observe a torn file — the replace is atomic, so they see either the
        /// whole old file or the whole new one.
        /// </para>
        /// </remarks>
        public string ReadAllText(string path)
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }
}
