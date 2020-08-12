using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Item;
using Hedron.Core.Entity.Property;
using Hedron.Skills;
using Hedron.Skills.Passive;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Combat
{
	/// <summary>
	/// Helper class for combat processing
	/// </summary>
	public static class CombatHelper
	{
		/// <summary>
		/// The threshold value to use for calculating multiple attacks
		/// </summary>
		public const int MULT_ATTACK_THRESHOLD = 1000;

		/// <summary>
		/// Calculates the entity's maximum possible number of attacks
		/// </summary>
		/// <param name="entity">The entity to calculate attacks for</param>
		/// <returns>The maximum number of possible attacks</returns>
		public static int CalcEntityMaxNumAttacks(EntityAnimate entity)
		{
			List<ItemWeapon> weapons = entity.GetItemsEquippedAt(ItemSlot.OneHandedWeapon, ItemSlot.TwoHandedWeapon).Cast<ItemWeapon>().ToList();

			// The total base attack rating to start with
			int attackRating = GetEntityBaseAttackRating(entity);

			// Calculate skill bonuses
			if (weapons.Count > 1)
			{
				// if dual wielding, take the better of the two weapons' skill ratings as the bonus, and half the dual rating of the dual wield skill
				int highestAttackRating = 0;
				
				foreach (var w in weapons)
				{
					double skill = SkillHelper.SkillOfWeapon(entity, w.WeaponType);
					if (skill > highestAttackRating)
						highestAttackRating = (int)skill;
				}

				attackRating += highestAttackRating;

				var dualWield = entity.Skills.FirstOrDefault(s => s.GetType() == typeof(DualWield));
				if (dualWield != null)
					attackRating += (int)dualWield.SkillLevel / 2;
			}
			else if (weapons.Count == 1)
			{
				// If single wielding, take the weapon's skill rating as the bonus, and if applicable, half the two-handed rating
				var weapon = weapons[0];
				double skill = SkillHelper.SkillOfWeapon(entity, weapon.WeaponType);

				if (skill > 0)
					attackRating += (int)skill;

				if (weapon.Slot == ItemSlot.TwoHandedWeapon)
				{
					var twoHanded = entity.Skills.FirstOrDefault(s => s.GetType() == typeof(TwoHanded));
					if (twoHanded != null)
						attackRating += (int)twoHanded.SkillLevel / 2;
				}
			}
			else
			{
				// Take the unarmed skill rating as the bonus
				double skill = SkillHelper.SkillOfWeapon(entity, WeaponType.Unarmed);

				// TODO: Add a skill that allows for a similar effect as two-handed or dual wield for unarmed multiple attacks
				if (skill > 0)
					attackRating += (int)skill;
			}

			// The total possible number of attacks, minimum 1
			return (int)Math.Ceiling((double)attackRating / MULT_ATTACK_THRESHOLD);
		}

		/// <summary>
		/// Provides an entity's base attack rating excluding weapons and skills
		/// </summary>
		/// <param name="entity">The entity to calculate attack rating for</param>
		/// <returns>The entity's base attack rating, or 0 if the entity was null.</returns>
		public static int GetEntityBaseAttackRating(EntityAnimate entity) => entity != null ? (int)entity.ModifiedQualities.AttackRating + ((int)entity.ModifiedAttributes.Finesse * 2) : 0;

		/// <summary>
		/// Provides an entity's total modified attack rating for a particular weapon
		/// </summary>
		/// <param name="entity">The entity to calculate from</param>
		/// <param name="weapon">The weapon to use when calculating rating</param>
		/// <returns>The total attack rating modified by associated skills</returns>
		/// <remarks>If this is an unarmed attack with no unarmed weapons, provide 'null' for the weapon.
		/// Dual Wielding and Two Handed will presently not be included in modifying the attack rating.
		/// The skill rating is reduced by a factor of 4 before improving attack rating.</remarks>
		/// TODO: This could probably be refactored by putting calculated code inside the entity's ModifiedQualities.AttackRating property.
		public static int GetEntityAttackRatingFor(EntityAnimate entity, ItemWeapon weapon)
		{
			if (entity == null)
				return GetEntityBaseAttackRating(entity);

			int baseAttackRating = GetEntityBaseAttackRating(entity);
			double skill;

			if (weapon == null)
				skill = SkillHelper.SkillOfWeapon(entity, WeaponType.Unarmed);
			else
				skill = SkillHelper.SkillOfWeapon(entity, weapon.WeaponType);

			if (skill < 0)
				skill = 0;

			return baseAttackRating + (int)(skill / 4);
		}

		/// <summary>
		/// Calculates the chances of getting an attack
		/// </summary>
		/// <param name="attackNumber">For potential multiple attack scenarios, which attack this is in the sequence</param>
		/// <param name="attackRating">The attack rating to calculate chances from</param>
		/// <param name="maxPossibleAttacks"></param>
		/// <returns>The chance of getting the attack</returns>
		/// <remarks>If this is the first attack, the chance will be 100% (1). Subsequent attacks are capped at 90% (0.9).</remarks>
		public static double CalculateAttackChance(int attackRating, int attackNumber)
		{
			if (attackNumber == 0)
				return 1;

			var attack = attackRating / (CombatHelper.MULT_ATTACK_THRESHOLD * Math.Pow(attackNumber + 1, 2));

			if (attack > 0.9)
				attack = 0.9;

			return attack;
		}

		/// <summary>
		/// Calculates damage for an attack, reduced by the defender's defense rating
		/// </summary>
		/// <param name="damage">The damage of the attack</param>
		/// <param name="defenseRating">The defense rating for reduction of damage</param>
		/// <returns>The damage dealt and the defense's reduction percentage of the attack</returns>
		/// <remarks>The percentage is expressed as a number between 0 and 1</remarks>
		public static (int, double) CalculateAttackDamageVsDefense(int damage, int defenseRating)
		{
			if (defenseRating <= 0)
				defenseRating = 1;

			double defenseReduction = defenseRating / (defenseRating + 500);
			return (damage * (int)defenseReduction, defenseReduction);
		}
	}
}