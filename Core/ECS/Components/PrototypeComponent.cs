using Hedron.Data;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing prototype/instance relationship information
    /// </summary>
    public class PrototypeComponent : IComponent
    {
        /// <summary>
        /// The instance ID of the object (null for prototypes)
        /// </summary>
        public uint? Instance { get; set; }

        /// <summary>
        /// The prototype ID of the object
        /// </summary>
        public uint? Prototype { get; set; }

        /// <summary>
        /// The cache type of the object
        /// </summary>
        public CacheType CacheType { get; set; }

        /// <summary>
        /// Whether this object is a prototype (template) or an instance
        /// </summary>
        public bool IsPrototype => CacheType == CacheType.Prototype;
    }
}