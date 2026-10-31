namespace Hedron.Core.Modules.Admin.Systems
{
    /// <summary>
    /// Domain system for runtime area authoring. All methods mutate entity/component state only;
    /// event publication is the caller's responsibility.
    /// </summary>
    public interface IAreaBuilderSystem
    {
        AreaCreationResult CreateArea(string name);

        /// <summary>Sets the area's name on its registered template and its live <c>AreaComponent</c> (INV-21).</summary>
        AreaPropertyResult SetAreaName(string areaBlueprintId, string name);

        /// <summary>Sets the area's description on its registered template and its live <c>AreaComponent</c> (INV-21).</summary>
        AreaPropertyResult SetAreaDescription(string areaBlueprintId, string description);

        /// <summary>
        /// Sets (or, with <c>null</c>, clears) the area's designation on its registered template and
        /// adds, replaces or removes the live <c>AreaDesignationComponent</c> (INV-21). Refuses a
        /// designation failing <see cref="Hedron.Core.Modules.World.AreaDesignationValidation"/>,
        /// changing nothing (R2).
        /// </summary>
        AreaPropertyResult SetAreaDesignation(string areaBlueprintId, Hedron.Core.Modules.World.AreaDesignation? designation);
    }

    /// <summary>
    /// Outcome of an area property mutation. On success <see cref="Template"/> is the mutated
    /// registered template (for the caller to write) and <see cref="AreaEntityId"/> the live area
    /// entity, or 0 when none is spawned. On refusal nothing changed.
    /// </summary>
    public sealed record AreaPropertyResult(
        bool Success,
        string? Error,
        string AreaBlueprintId,
        uint AreaEntityId,
        Hedron.Core.Modules.World.Templates.AreaTemplate? Template)
    {
        public static AreaPropertyResult Refused(string areaBlueprintId, string error) =>
            new(false, error, areaBlueprintId, 0, null);
    }

    /// <summary>Result of <see cref="IAreaBuilderSystem.CreateArea"/>.</summary>
    public readonly record struct AreaCreationResult(
        uint AreaEntityId,
        string BlueprintId,
        Hedron.Core.Modules.World.Templates.AreaTemplate Template);
}
