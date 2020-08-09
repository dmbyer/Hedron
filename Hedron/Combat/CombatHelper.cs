using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Item;
using Hedron.Core.Entity.Living;
using Hedron.Core.Entity.Property;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Skills;
using Hedron.Skills.Passive;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Combat
{
	/// <summary>
	/// 
	/// </summary>
	public static class CombatHelper
	{
		/// <summary>
		/// The threshold value to use for calculating multiple attacks
		/// </summary>
		private const int MULT_ATTACK_THRESHOLD = 1000;

		/// <summary>
		/// Calculates the number of autoattacks
		/// </summary>
		/// <param name="entity">The entity to calculate from</param>
		/// <param name="isOffhand">Whether the calculation should be done as the offhand weapon</param>
		/// <returns>The number of attacks the entity made.</returns>
		/// <remarks>If this is the offhand weapon, the number of attacks can be 0. Otherwise, it will be at least 1.</remarks>
		public static int CalcNumAttacks(EntityAnimate entity, WeaponType weapon, bool isOffhand)
		{
			// The total base attack rating to start with
			int attackRating = SkillHelper.SkillOfWeapon(entity, weapon) + (int)entity.ModifiedQualities.AttackRating + ((int)entity.ModifiedAttributes.Finesse * 2);
			Random roll = World.Random;

			if (isOffhand)
			{
				attackRating += entity.Skills.Where(s => s.GetType() == typeof(DualWield)).FirstOrDefault().SkillLevel;
				attackRating /= 2;
			}

			// The total possible number of attacks; there is always a non-zero chance of an extra attack
			int possibleExtraAttacks = (int)Math.Ceiling((double)attackRating / MULT_ATTACK_THRESHOLD);
			int attacks = isOffhand ? 0 : 1;

			// Calculate the chances for each possible attack and increment the attacks accordingly
			for (int attIndex = 0; attIndex < possibleExtraAttacks; attIndex++)
			{
				double chance = attackRating / (MULT_ATTACK_THRESHOLD * Math.Pow(attIndex + 1, 2));
				// As percentile
				chance *= 100;

				// Maximum of 90% chance for any given multiple attack
				if (chance > 90)
					chance = 90;

				attacks += roll.Next(1, 100) <= chance ? 1 : 0;
			}

			return attacks;
		}
	}
}