using Core.ECS.Properties;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing pool information (HP, Stamina, Energy) for entities
    /// </summary>
    public class PoolsComponent : IComponent
    {
        /// <summary>
        /// The entity's base maximum pools
        /// </summary>
        public Pools BaseMaxPools { get; set; } = Pools.Default();

        /// <summary>
        /// The entity's current pools
        /// </summary>
        public Pools CurrentPools { get; set; } = Pools.Default();
    }
}