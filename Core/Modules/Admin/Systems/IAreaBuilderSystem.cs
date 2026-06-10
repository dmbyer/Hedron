namespace Hedron.Core.Modules.Admin.Systems
{
    /// <summary>
    /// Domain system for runtime area authoring. All methods mutate entity/component state only;
    /// event publication is the caller's responsibility.
    /// </summary>
    public interface IAreaBuilderSystem
    {
        AreaCreationResult CreateArea(string name);
    }

    /// <summary>Result of <see cref="IAreaBuilderSystem.CreateArea"/>.</summary>
    public readonly record struct AreaCreationResult(
        uint AreaEntityId,
        string BlueprintId,
        Hedron.Core.Modules.World.Templates.AreaTemplate Template);
}
