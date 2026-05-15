using Hedron.Core.ECS;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// An authored blueprint that can be applied to a freshly created entity to attach
    /// the components an archetype requires. Implementations live next to the module
    /// that owns the archetype (e.g. <c>RoomTemplate</c> in the World module).
    /// </summary>
    /// <remarks>
    /// <see cref="TemplateRegistry"/> attaches the cross-cutting <c>BlueprintComponent</c>
    /// before calling <see cref="Apply"/>; templates only attach archetype-specific components.
    /// </remarks>
    public interface IEntityTemplate
    {
        /// <summary>Stable string id (e.g. <c>"room.crossroads"</c>, <c>"area.starter_road"</c>).</summary>
        string BlueprintId { get; }

        /// <summary>Attaches the archetype's components to <paramref name="entity"/>.</summary>
        void Apply(Entity entity, EntityService entityService);
    }
}
