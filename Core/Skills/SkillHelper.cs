﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Entities.Base;
using Hedron.Core.Skills;
using Hedron.Core.Skills.Passive;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Commands;
using Hedron.Core.System;

namespace Hedron.Core.Skills
{
	public static class SkillHelper
	{

		/// <summary>
		/// Retrieves the skill level of an entity's weapon
		/// </summary>
		/// <param name="entity">The entity to check the skill of</param>
		/// <param name="weaponType">The weapon type to be checked</param>
		/// <returns>The associated weapon's skill level, or Constants.SKILL_UNLEARNED if not found.</returns>
		public static double SkillOfWeapon(EntityAnimate entity, WeaponType weaponType)
		{
			ISkill skill;

			switch (weaponType)
			{
				case WeaponType.Axe:
					skill = entity.Skills.FirstOrDefault(s => s.GetType() == typeof(Axe));
					break;
				case WeaponType.Bow:
					skill = entity.Skills.FirstOrDefault(s => s.GetType() == typeof(Bow));
					break;
				case WeaponType.Dagger:
					skill = entity.Skills.FirstOrDefault(s => s.GetType() == typeof(Dagger));
					break;
				case WeaponType.Mace:
					skill = entity.Skills.FirstOrDefault(s => s.GetType() == typeof(Mace));
					break;
				case WeaponType.Staff:
					skill = entity.Skills.FirstOrDefault(s => s.GetType() == typeof(Staff));
					break;
				case WeaponType.Sword:
					skill = entity.Skills.FirstOrDefault(s => s.GetType() == typeof(Sword));
					break;
				case WeaponType.Unarmed:
					skill = entity.Skills.FirstOrDefault(s => s.GetType() == typeof(Unarmed));
					break;
				case WeaponType.Wand:
					skill = entity.Skills.FirstOrDefault(s => s.GetType() == typeof(Wand));
					break;
				default:
					return Constants.SKILL_UNLEARNED;
			}

			return skill?.SkillLevel ?? Constants.SKILL_UNLEARNED;
		}
	}
}