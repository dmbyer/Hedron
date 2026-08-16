namespace Hedron.Core.Modules.Authoring.Contracts
{
    /// <summary>
    /// Maps one content kind between its <see cref="ContentDefinition"/> (the authored template) and
    /// a flat, transport-shaped DTO. <strong>This is the kind-dispatch seam</strong> for any
    /// out-of-process authoring surface: an endpoint tier resolves
    /// <c>IContentDefinitionMapper&lt;TDto&gt;</c> from DI and stays generic, so adding a second
    /// writable kind is a new DTO + mapper + registration — never a <c>switch</c> in an entry-point
    /// surface, which <c>docs/architecture/08-blazor.md</c> forbids.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mapping exists because <see cref="ContentDefinition"/> cannot round-trip unmapped: it has
    /// no parameterless constructor, its <c>BlueprintId</c> is derived and get-only, and it wraps a
    /// polymorphic <c>IEntityTemplate</c> discriminated by <see cref="ContentKind"/>.
    /// </para>
    /// <para>
    /// A mapper is <strong>pure translation and nothing else</strong> — no validation, no id minting,
    /// no file access. Those stay in <c>IContentDefinitionCatalog</c> and <c>IContentValidator</c>,
    /// so a definition written over HTTP obeys exactly the rules one written in-process does
    /// (INV-15/INV-19).
    /// </para>
    /// </remarks>
    /// <typeparam name="TDto">
    /// The transport shape for this kind. Mutable properties with a parameterless constructor —
    /// it is bound from a request body and published in the OpenAPI document.
    /// </typeparam>
    public interface IContentDefinitionMapper<TDto> where TDto : class, new()
    {
        /// <summary>The content kind this mapper translates.</summary>
        ContentKind Kind { get; }

        /// <summary>Projects an authored definition onto its transport shape.</summary>
        TDto ToDto(ContentDefinition definition);

        /// <summary>
        /// Builds an authored definition carrying <paramref name="blueprintId"/> from a transport
        /// shape. The id is supplied by the caller rather than read from <paramref name="dto"/>:
        /// on an update it comes from the route, and on a create it is either the caller's chosen id
        /// or one minted by the catalog. Any id on <paramref name="dto"/> is ignored.
        /// </summary>
        ContentDefinition ToDefinition(TDto dto, string blueprintId);
    }
}
