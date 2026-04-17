using Core.ECS.Properties;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing attribute information for entities
    /// </summary>
    public class AttributesComponent : IComponent
    {
        /// <summary>
        /// The entity's base attributes
        /// </summary>
        public Attributes BaseAttributes { get; set; } = Attributes.Default();

        /// <summary>
        /// Whether these attributes are modifiers
        /// </summary>
        public bool IsMultiplier { get; set; }
    }
}