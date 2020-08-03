using Hedron.Skills;
using Hedron.Skills.Passive;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Commands
{
    public static class SkillMap
    {
        /// <summary>
        /// The list of all available active skills
        /// </summary>
        public static Dictionary<string, ISkill> ActiveSkills = new Dictionary<string, ISkill>()
        {

        };

        /// <summary>
        /// The list of all available passive skills
        /// </summary>
        public static Dictionary<string, Type> PassiveSkills = new Dictionary<string, Type>()
        {
            { "dodge", typeof(Dodge) }
        };
    }
}
