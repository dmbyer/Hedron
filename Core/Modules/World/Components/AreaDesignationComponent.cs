using Hedron.Core.ECS;

namespace Hedron.Core.Modules.World.Components
{
    /// <summary>
    /// The live form of <see cref="Templates.AreaTemplate.Designation"/>, attached to an area entity
    /// by <c>AreaTemplate.Apply</c> only when the area is designated (mirrors
    /// <c>AspectAffinitiesComponent</c>). World content: deliberately not <c>[Persistent]</c>, and
    /// area entities never carry <c>PersistentEntity</c> (INV-23) — the YAML is the durable form.
    /// </summary>
    public sealed class AreaDesignationComponent : IComponent
    {
        public AreaDesignation Designation { get; set; } = null!;
    }
}
