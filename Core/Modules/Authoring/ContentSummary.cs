namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// A lightweight listing row for one authored definition on disk: its blueprint id, display
    /// name, description, and the area blueprint id it belongs to. Produced by
    /// <see cref="Systems.IContentDefinitionCatalog.List"/>; any truncation to a "short"
    /// description is a presentation concern left to the caller.
    /// </summary>
    /// <param name="BlueprintId">The definition's own blueprint id.</param>
    /// <param name="Name">Human-readable display name.</param>
    /// <param name="Description">Full description text (truncation is the caller's concern).</param>
    /// <param name="AreaBlueprintId">
    /// The area this definition belongs to, or <c>null</c> when unknown or not applicable.
    /// Rooms carry their own <c>AreaId</c> directly; items and mobs resolve through
    /// <c>SpawnRoomBlueprintId</c> → that room's <c>AreaId</c> (two-hop). Areas always yield
    /// <c>null</c> (they have no parent area). Missing, blank, or dangling references also yield
    /// <c>null</c>.
    /// </param>
    public sealed record ContentSummary(
        string BlueprintId,
        string Name,
        string Description,
        string? AreaBlueprintId = null);
}
