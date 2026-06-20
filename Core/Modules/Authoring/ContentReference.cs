namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// Declares one typed reference edge over the on-disk YAML definition set: a (source kind,
    /// field label) → target kind relationship. Instances are data — the
    /// <see cref="Systems.ContentReferenceIndex"/> holds the declared set and uses it to drive
    /// resolution, referrer lookup, and broken-link sweeps without per-edge code paths.
    /// </summary>
    /// <param name="SourceKind">The kind of definition that carries the reference field.</param>
    /// <param name="FieldLabel">
    /// Human-readable name for the reference field, used in broken-reference reports
    /// (e.g. <c>"AreaId"</c>, <c>"Exits[North]"</c>, <c>"SpawnRoomBlueprintId"</c>).
    /// </param>
    /// <param name="TargetKind">The kind the reference points at.</param>
    public sealed record ReferenceEdge(ContentKind SourceKind, string FieldLabel, ContentKind TargetKind);

    /// <summary>
    /// One dangling (broken) cross-definition reference detected by the reference index.
    /// </summary>
    /// <param name="SourceKind">Kind of the definition that holds the broken reference.</param>
    /// <param name="SourceBlueprintId">Blueprint id of the definition that holds the broken reference.</param>
    /// <param name="FieldLabel">
    /// The field / selector that carries the broken reference
    /// (e.g. <c>"AreaId"</c>, <c>"Exits[East]"</c>, <c>"SpawnRoomBlueprintId"</c>).
    /// </param>
    /// <param name="MissingTargetId">The target blueprint id that does not resolve on disk.</param>
    public sealed record BrokenReference(
        ContentKind SourceKind,
        string SourceBlueprintId,
        string FieldLabel,
        string MissingTargetId);

    /// <summary>
    /// Describes one referrer of a given definition — both who is referencing it and via which
    /// field. Used by WP2's delete-cascade path to enumerate the edits that must clear the
    /// dangling link once a definition is deleted.
    /// </summary>
    /// <param name="ReferrerKind">Kind of the definition that holds the reference.</param>
    /// <param name="ReferrerBlueprintId">Blueprint id of the referring definition.</param>
    /// <param name="FieldLabel">
    /// The field / selector that carries the reference
    /// (e.g. <c>"AreaId"</c>, <c>"Exits[North]"</c>, <c>"SpawnRoomBlueprintId"</c>, <c>"Rooms"</c>).
    /// </param>
    public sealed record ReferrerEdit(
        ContentKind ReferrerKind,
        string ReferrerBlueprintId,
        string FieldLabel);
}
