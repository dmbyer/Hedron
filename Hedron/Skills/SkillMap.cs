using Hedron.Core.Entity.Property;
using Hedron.Skills;
using Hedron.Skills.Passive;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Commands
{
    public static class SkillMap
    {
        /// <summary>
        /// The list of all available active skills
        /// </summary>
        public static Dictionary<string, Type> ActiveSkills = new Dictionary<string, Type>()
        {

        };

        /// <summary>
        /// The list of all available passive skills
        /// </summary>
        private static Dictionary<string, Type> PassiveSkills = new Dictionary<string, Type>()
        {
            { "axe",        typeof(Axe)       },
            { "bow",        typeof(Bow)       },
            { "dagger",     typeof(Dagger)    },
            { "dodge",      typeof(Dodge)     },
            { "dual wield", typeof(DualWield) },
            { "mace",       typeof(Mace)      },
            { "one handed", typeof(OneHanded) },
            { "shield",     typeof(Shield)    },
            { "staff",      typeof(Staff)     },
            { "sword",      typeof(Sword)     },
            { "two handed", typeof(TwoHanded) },
            { "unarmed",    typeof(Unarmed)   },
            { "wand",       typeof(Wand)      }
        };

        /// <summary>
        /// Maps a weapon type to its associated skill name
        /// </summary>
        private static Dictionary<WeaponType, string> WeaponTypeSkillMap = new Dictionary<WeaponType, string>()
        {
            { WeaponType.Axe,     "axe"     },
            { WeaponType.Bow,     "bow"     },
            { WeaponType.Dagger,  "dagger"  },
            { WeaponType.Mace,    "mace"    },
            { WeaponType.Staff,   "staff"   },
            { WeaponType.Sword,   "sword"   },
            { WeaponType.Unarmed, "unarmed" },
            { WeaponType.Wand,    "wand"    },
        };

        /// <summary>
        /// Maps a skill class type to a skill name
        /// </summary>
        /// <param name="skill">The class type of the skill</param>
        /// <returns>The name of the associated skill</returns>
        public static string SkillToFriendlyName(Type skill)
        {
            var match = ActiveSkills.FirstOrDefault(kvp => kvp.Value == skill);

            if (match.Key == null)
                match = PassiveSkills.FirstOrDefault(kvp => kvp.Value == skill);

            if (match.Key == null)
                throw new ArgumentException($"Invalid skill type. Update {nameof(SkillMap)} with proper skill names and types.", nameof(skill));

            return match.Key;
        }

        /// <summary>
        /// Maps a skill name to the skill class type
        /// </summary>
        /// <param name="skill">The name of the skill</param>
        /// <returns>The type of the associated skill</returns>
        public static Type FriendlyNameToSkill(string skill)
        {
            var match = ActiveSkills.FirstOrDefault(kvp => kvp.Key == skill);
            
            if (match.Key == null)
                match = PassiveSkills.FirstOrDefault(kvp => kvp.Key == skill);

            if (match.Key == null)
                throw new ArgumentException($"Invalid skill name. Update {nameof(SkillMap)} with proper skill names and types.", skill);

            return match.Value;
        }

        /// <summary>
        /// Maps a weapon type to the associated weapon skill's name
        /// </summary>
        /// <param name="weaponType">The weapon type to lookup</param>
        /// <returns>The name of the skill for the weapon</returns>
        public static string WeaponTypeToSkillName(WeaponType weaponType) => WeaponTypeSkillMap.First(kvp => kvp.Key == weaponType).Value;

        /// <summary>
        /// Mape a weapon type to the associated weapon skill's type
        /// </summary>
        /// <param name="weaponType">The weapon type to lookup</param>
        /// <returns>The type of the skill for the weapon</returns>
        public static Type WeaponTypeToSkillType(WeaponType weaponType)
        {
            var match = FriendlyNameToSkill(WeaponTypeToSkillName(weaponType));

            if (match == null)
                throw new ArgumentException($"Invalid weapon type. Update {nameof(WeaponTypeSkillMap)} with proper skill names and weapon types.", nameof(weaponType));

            return match;
        }
    }
}