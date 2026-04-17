using System.Collections.Generic;
using Core.Modules.Skills;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing skill information for entities
    /// </summary>
    public class SkillsComponent : IComponent
    {
        /// <summary>
        /// The entity's learned skills
        /// </summary>
        public List<ISkill> Skills { get; set; } = new List<ISkill>();
    }
}