using Core.ECS.Properties;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing quality information for entities
    /// </summary>
    public class QualitiesComponent : IComponent
    {
        /// <summary>
        /// The entity's base qualities
        /// </summary>
        public Qualities BaseQualities { get; set; } = Qualities.Default();
    }
}