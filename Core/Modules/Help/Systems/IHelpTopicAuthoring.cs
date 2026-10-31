using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Output;
using Hedron.Core.Systems.Definitions;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>Editable fields of a world help topic, as the editor submits them.</summary>
    public sealed record HelpTopicDraft(
        string Id,
        string Title,
        IReadOnlyList<string> Aliases,
        IReadOnlyList<string> SeeAlso,
        string Body);

    /// <summary>Both layers merged as the game would see them, plus every file-level diagnostic.</summary>
    public sealed record HelpTopicCatalogView(IReadOnlyList<HelpTopic> Topics, IReadOnlyList<ContentDiagnostic> Diagnostics);

    /// <summary>One topic opened in the editor.</summary>
    /// <param name="Editable">False for an engine topic (read-only; offer Override).</param>
    /// <param name="OverridesEngine">A world topic that replaces an engine topic (Delete restores the engine version).</param>
    public sealed record HelpTopicEditView(HelpTopic Topic, bool Editable, bool OverridesEngine);

    /// <summary>
    /// Authoring for world help topics (INV-18), the editor's thin-caller seam. Reads disk on every
    /// call (disk is the editor's truth; the registry is the game's), applies the same
    /// <see cref="HelpTopicSetBuilder"/> rules as boot and <c>reload</c>, and writes the
    /// <b>world</b> layer only — the engine layer is never written. Writes take effect in-game on the
    /// next <c>reload</c>/Apply. Writes are serialized by the module's own gate (INV-31); last save wins.
    /// </summary>
    public interface IHelpTopicAuthoring
    {
        HelpTopicCatalogView List();

        HelpTopicEditView? Load(string id);

        /// <summary>The player rendering of <paramref name="draft"/> against the current topics.</summary>
        HelpTopicMessage Preview(HelpTopicDraft draft);

        /// <summary>Blocking problems with <paramref name="draft"/> (empty = saveable).</summary>
        IReadOnlyList<string> Validate(HelpTopicDraft draft);

        /// <summary>
        /// Writes <c>&lt;world&gt;/&lt;id&gt;.yaml</c>. <paramref name="previousId"/> renames: the old world
        /// file is deleted after the new write succeeds. Returns the R10 warnings for this topic.
        /// </summary>
        Task<ContentWriteResult> SaveAsync(HelpTopicDraft draft, string? previousId = null, CancellationToken ct = default);

        /// <summary>Copies an engine topic into the world layer for editing; refused if a world copy exists.</summary>
        Task<ContentWriteResult> OverrideAsync(string engineId, CancellationToken ct = default);

        /// <summary>Deletes a world topic (an override's engine version returns); refused for engine topics.</summary>
        Task<ContentWriteResult> DeleteAsync(string id, CancellationToken ct = default);
    }
}
