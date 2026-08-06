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

        /// <summary>Reads the full text of <paramref name="path"/>.</summary>
        string ReadAllText(string path);
    }

    /// <summary>
    /// Production <see cref="IContentFileReader"/> — a direct pass-through to <c>System.IO</c>.
    /// </summary>
    public sealed class ContentFileReader : IContentFileReader
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public IReadOnlyList<string> GetFiles(string directory, string searchPattern) =>
            Directory.Exists(directory)
                ? Directory.GetFiles(directory, searchPattern)
                : Array.Empty<string>();

        public bool FileExists(string path) => File.Exists(path);

        public string ReadAllText(string path) => File.ReadAllText(path);
    }
}
