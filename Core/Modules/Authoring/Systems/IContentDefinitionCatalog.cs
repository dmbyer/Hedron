using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Modules.World.Templates;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// The single backing for offline content authoring: read / list / load / create / validate /
    /// write / delete over the YAML content-definition families (area, room, item, mob). Both the
    /// offline Blazor editor and the headless bulk generator are thin callers of this facade — no
    /// authoring logic lives in a UI component or a generator trigger.
    /// </summary>
    /// <remarks>
    /// The catalog writes YAML only. It never creates a live entity, adds <c>PersistentEntity</c>,
    /// or calls <c>SaveEntityAsync</c> (INV-12/22/23) — applying content to the live world is a
    /// separate <c>reload</c> step. Per-kind specifics (which writer, which template) are dispatched
    /// inside the catalog by <see cref="ContentKind"/>.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <strong>Read semantics are cached, not disk-truth.</strong> The default implementation keeps
    /// an in-memory index so a render pass does not re-sweep the corpus per call. Every catalog
    /// mutator drops the <em>whole</em> index (writes cascade across files, so entry-scoped
    /// invalidation cannot express them), which makes reads coherent for catalog-mediated writes.
    /// Writes that bypass the catalog — the <c>generate</c> CLI in its own process, the game host's
    /// <c>mk*</c>/<c>set*</c>/<c>dig</c> verbs writing through the <c>I*ContentWriter</c> family —
    /// do <em>not</em> invalidate it; call <see cref="Invalidate"/> to pick those up.
    /// </para>
    /// <para>
    /// <strong>Concurrency (INV-31).</strong> The index is mutable state on a DI singleton reached
    /// concurrently from multiple Blazor circuits. The implementation guards index consistency
    /// only — it does not make the multi-file write cascade atomic.
    /// </para>
    /// </remarks>
    public interface IContentDefinitionCatalog
    {
        /// <summary>
        /// Enumerates the definitions of <paramref name="kind"/> present on disk. Each returned
        /// <see cref="ContentSummary"/> carries the resolved <see cref="ContentSummary.AreaBlueprintId"/>
        /// for rooms (one-hop via <c>RoomTemplate.AreaId</c>), items, and mobs (two-hop via
        /// <c>SpawnRoomBlueprintId</c> → that room's <c>AreaId</c>). Areas always yield
        /// <c>null</c>; missing/blank/dangling references also yield <c>null</c>.
        /// </summary>
        IReadOnlyList<ContentSummary> List(ContentKind kind);

        /// <summary>
        /// Returns the rooms whose resolved <see cref="ContentSummary.AreaBlueprintId"/> equals
        /// <paramref name="areaBlueprintId"/>. Returns an empty list for an unknown area id.
        /// </summary>
        IReadOnlyList<ContentSummary> RoomsInArea(string areaBlueprintId);

        /// <summary>Loads one definition by blueprint id, or <c>null</c> if no file exists.</summary>
        ContentDefinition? Load(ContentKind kind, string blueprintId);

        /// <summary>
        /// Validates then writes a definition to its YAML file. Refuses to write (and returns a
        /// failed result carrying the validation errors) when structural validation fails.
        /// On structural pass, calls <see cref="IContentReferenceIndex.BrokenFor"/> and folds any
        /// dangling cross-references into <see cref="ContentWriteResult.Warnings"/> — the file is
        /// still written (warn-but-allow; INV-19). Use <see cref="SaveRoomAsync"/> for a room with
        /// the optional bidirectional exit-linking behavior.
        /// </summary>
        Task<ContentWriteResult> SaveAsync(ContentDefinition definition, CancellationToken ct = default);

        /// <summary>
        /// Validates then writes a <see cref="RoomTemplate"/>. Behaves identically to
        /// <see cref="SaveAsync"/> for the room itself; when <paramref name="bidirectional"/> is
        /// <c>true</c>, also writes the inverse exit on each target room via
        /// <see cref="IRoomContentWriter"/> and <see cref="DirectionExtensions.Opposite"/>.
        /// <para>
        /// Conflict policy: if a target room already has a <em>different</em> exit in the inverse
        /// direction, that paired write is skipped and a warning is added to the result. If the
        /// target already has the <em>correct</em> inverse exit (or this is a self-loop), the
        /// paired write is a silent no-op (no warning, no spurious rewrite).
        /// </para>
        /// </summary>
        Task<ContentWriteResult> SaveRoomAsync(
            RoomTemplate room,
            bool bidirectional,
            CancellationToken ct = default);

        /// <summary>
        /// Deletes the YAML file for <paramref name="blueprintId"/> of <paramref name="kind"/> and
        /// cascade-clears every definition that references it:
        /// <list type="bullet">
        ///   <item>Room referencing via <c>AreaId</c> → <c>AreaId</c> cleared to empty.</item>
        ///   <item>Room referencing via <c>Exits[dir]</c> → that exit entry removed.</item>
        ///   <item>Item or mob with <c>SpawnRoomBlueprintId == blueprintId</c> → field cleared.</item>
        ///   <item>Area with <c>blueprintId</c> in its <c>Rooms</c> list → entry removed.</item>
        /// </list>
        /// Returns a <see cref="ContentDeleteResult"/> describing the deleted file and each cascade
        /// edit. <strong>YAML-file operation only</strong> — no <c>EntityService.DestroyEntity</c>,
        /// no SQLite delete, no live-world mutation (INV-22/23).
        /// </summary>
        Task<ContentDeleteResult> DeleteAsync(ContentKind kind, string blueprintId, CancellationToken ct = default);

        /// <summary>
        /// Produces a new, un-persisted definition with a freshly minted ad-hoc blueprint id and the
        /// given name. Creates no live entity and registers nothing — call <see cref="SaveAsync"/> to
        /// persist it.
        /// </summary>
        ContentDefinition CreateNew(ContentKind kind, string name);

        /// <summary>
        /// Produces a new, un-persisted definition with the given name. When
        /// <paramref name="blueprintId"/> is non-null/non-empty, the definition carries that
        /// deliberate id (validated by <see cref="CreateAsync"/> at write time, not here); when
        /// <c>null</c> or empty, falls back to a freshly minted ad-hoc id (same as
        /// <see cref="CreateNew(ContentKind, string)"/>). Creates no live entity and registers
        /// nothing — call <see cref="CreateAsync"/> to persist it.
        /// </summary>
        ContentDefinition CreateNew(ContentKind kind, string name, string? blueprintId);

        /// <summary>
        /// Create-guarded write: validates <paramref name="definition"/>'s blueprint id via
        /// <see cref="Hedron.Core.Modules.World.Systems.IContentValidator.ValidateBlueprintId"/>
        /// and refuses (no write) when it is malformed or already resolves on disk for the same
        /// kind (collision → refuse, no merge/overwrite). Use this for first-write creation with
        /// a deliberate id; <see cref="SaveAsync"/> remains the overwrite-on-edit path.
        /// </summary>
        Task<ContentWriteResult> CreateAsync(ContentDefinition definition, CancellationToken ct = default);

        /// <summary>
        /// Renames the YAML definition for <paramref name="oldId"/> of <paramref name="kind"/> to
        /// <paramref name="newId"/>: writes a new file carrying the definition's full state (with
        /// its own self-referential fields rewritten <c>oldId → newId</c>), rewrites every
        /// external referrer found via <see cref="IContentReferenceIndex.Referrers"/> to point at
        /// <paramref name="newId"/> (best-effort — a per-referrer failure is logged and skipped,
        /// matching <see cref="DeleteAsync"/>), then deletes the <paramref name="oldId"/> file.
        /// Refuses (no write, <paramref name="oldId"/> file intact) when <paramref name="newId"/>
        /// is malformed or already taken by a same-kind definition, or when no <paramref name="oldId"/>
        /// file exists. A kind-prefix mismatch on <paramref name="newId"/> is a non-blocking
        /// warning, not a refusal. Folds out-of-YAML advisories (e.g. renaming the room configured
        /// as <c>World:StartingRoomBlueprintId</c>) into <see cref="ContentRenameResult.Warnings"/>
        /// — <strong>YAML-file operation only</strong>, no SQLite write, no config write, no
        /// live-world mutation (INV-22/23); the live world re-keys on the next <c>reload</c>.
        /// </summary>
        Task<ContentRenameResult> RenameAsync(
            ContentKind kind,
            string oldId,
            string newId,
            CancellationToken ct = default);

        /// <summary>
        /// Removes one exit from a room — the mirror of <see cref="SaveRoomAsync"/>'s bidirectional
        /// <em>add</em> policy. Removing an absent exit is a no-op success (no file write). When
        /// <paramref name="bidirectional"/> is <c>true</c>, also removes the target room's inverse
        /// exit, but only when it still points back at <paramref name="roomBlueprintId"/> — an
        /// inverse pointing elsewhere (or already absent) is left untouched.
        /// </summary>
        Task<ContentWriteResult> RemoveRoomExitAsync(
            string roomBlueprintId,
            Direction direction,
            bool bidirectional,
            CancellationToken ct = default);

        /// <summary>
        /// Returns <paramref name="definition"/> with only its blueprint id replaced — every other
        /// authored field is preserved, and the definition's own self-referential ids (a room's
        /// self-loop exit) are rewritten to match, using the same rule
        /// <see cref="RenameAsync"/> applies. A <c>null</c>/blank <paramref name="blueprintId"/>
        /// falls back to a freshly minted ad-hoc id. Pure — writes nothing.
        /// </summary>
        ContentDefinition WithBlueprintId(ContentDefinition definition, string? blueprintId);

        /// <summary>
        /// Mints the next definition in a "save and create next" run: a fresh definition of
        /// <paramref name="previous"/>'s kind (id minted via <see cref="CreateNew(ContentKind, string)"/>)
        /// carrying <paramref name="name"/>, with the per-kind authoring context carried forward and
        /// everything else reset.
        /// <list type="bullet">
        ///   <item><strong>Area</strong> — nothing carries forward; areas are authored individually.</item>
        ///   <item><strong>Room</strong> — <c>AreaId</c> carries; name, description, exits, coordinates reset.</item>
        ///   <item><strong>Item</strong> — Tier, Band, <c>ItemType</c>, <c>WornSlots</c> carry; name,
        ///     description, stat bonuses, value reset.</item>
        ///   <item><strong>Mob</strong> — Tier, Band, <c>SpawnRoomBlueprintId</c> carry; name,
        ///     description, attributes, pools, loot, shop config reset.</item>
        /// </list>
        /// Which fields carry is authoring policy plus kind dispatch, so it lives here rather than
        /// in an editor component (see <c>docs/architecture/08-blazor.md</c>). Pure — writes nothing.
        /// </summary>
        ContentDefinition CreateNextFrom(ContentDefinition previous, string name);

        /// <summary>
        /// Drops the whole in-memory index so the next read re-populates from disk. The escape hatch
        /// for content written outside this process (the <c>generate</c> CLI, the game host's
        /// <c>mk*</c> verbs); catalog-mediated writes invalidate on their own.
        /// </summary>
        void Invalidate();
    }
}
