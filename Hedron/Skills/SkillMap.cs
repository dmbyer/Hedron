using Hedron.Skills;
using Hedron.Skills.Passive;
using System;
using System.Collections.Generic;

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
            { "axe", typeof(Axe) },
            { "bow", typeof(Bow) },
            { "dagger", typeof(Dagger) },
            { "dodge", typeof(Dodge) },
            { "dual wield", typeof(DualWield) },
            { "mace", typeof(Mace) },
            { "one handed", typeof(OneHanded) },
            { "shield", typeof(Shield) },
            { "staff", typeof(Staff) },
            { "sword", typeof(Sword) },
            { "two handed", typeof(TwoHanded) },
            { "unarmed", typeof(Unarmed) },
            { "wand", typeof(Wand) }
        };
    }
}