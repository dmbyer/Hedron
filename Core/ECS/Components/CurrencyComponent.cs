using Core.ECS.Properties;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing currency information for entities
    /// </summary>
    public class CurrencyComponent : IComponent
    {
        /// <summary>
        /// The entity's currency
        /// </summary>
        public Currency Currency { get; set; } = new Currency();
    }
}