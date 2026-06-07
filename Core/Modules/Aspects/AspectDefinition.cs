namespace Hedron.Core.Modules.Aspects
{
    public sealed record AspectDefinition(
        AspectId Id,
        string Name,
        string Description,
        AspectCategory Category
    );
}
