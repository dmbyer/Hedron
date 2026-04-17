using Core.ECS.Properties;
using Core.ECS.Properties.Behavior;
using Core.ECS.Properties.Effects;
using Hedron.Core.System;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing mob-specific data
    /// </summary>
    public class MobDataComponent : IComponent
    {
        /// <summary>
        /// The mob's behavior
        /// </summary>
        public MobBehavior Behavior { get; set; } = new MobBehavior();

        /// <summary>
        /// The mob's advancement level
        /// </summary>
        public MobLevel Level { get; set; } = MobLevel.Fair;

        /// <summary>
        /// The Effect for the mob's level
        /// </summary>
        public Effect LevelEffect { get; set; } = new Effect();
    }
}