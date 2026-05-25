using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Tracks items worn or wielded by a character, keyed by <see cref="WornSlot"/>.
    /// Cross-cutting — placed in Core/ECS/Components so both players and mobs can carry it
    /// without a domain dependency. Values are item entity ids.
    /// </summary>
    [Persistent]
    public sealed class EquipmentComponent : IComponent
    {
        public Dictionary<WornSlot, uint> Slots { get; set; } = new();
    }
}
