using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// An editable authored definition: a content <see cref="Kind"/> paired with the underlying
    /// <see cref="IEntityTemplate"/> (the durable blueprint POCO). The catalog produces these on
    /// <c>Load</c>/<c>CreateNew</c> and consumes them on <c>SaveAsync</c>; an editor binds form
    /// fields to the concrete template (cast by <see cref="Kind"/>).
    /// </summary>
    /// <remarks>
    /// Wrapping the template — rather than mutating the live world — is what keeps authoring off
    /// the heartbeat: a <see cref="ContentDefinition"/> never corresponds to a live entity (INV-12).
    /// </remarks>
    public sealed class ContentDefinition
    {
        public ContentKind Kind { get; }
        public IEntityTemplate Template { get; }
        public string BlueprintId => Template.BlueprintId;

        public ContentDefinition(ContentKind kind, IEntityTemplate template)
        {
            Kind = kind;
            Template = template;
        }
    }
}
