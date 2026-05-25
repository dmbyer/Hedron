using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Tracks item entities carried by a character. Cross-cutting — placed in Core/ECS/Components
    /// so BroadcastSystem and other core services can read inventory without a domain dependency.
    /// Items in inventory have no <see cref="LocationComponent"/>; their presence is recorded
    /// exclusively here.
    /// </summary>
    [Persistent]
    public sealed class InventoryComponent : IComponent
    {
        public List<uint> ItemEntityIds { get; set; } = new();
    }
}
