namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// A lightweight listing row for one authored definition on disk: its blueprint id, display
    /// name, and description. Produced by <see cref="Systems.IContentDefinitionCatalog.List"/>;
    /// any truncation to a "short" description is a presentation concern left to the caller.
    /// </summary>
    public sealed record ContentSummary(string BlueprintId, string Name, string Description);
}
