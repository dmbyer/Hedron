using System.Collections.Generic;
using Core.ECS.Properties.Effects;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing effects applied to entities
    /// </summary>
    public class EffectsComponent : IComponent
    {
        /// <summary>
        /// The list of effects on the entity
        /// </summary>
        public List<Effect> Effects { get; set; } = new List<Effect>();
    }
}