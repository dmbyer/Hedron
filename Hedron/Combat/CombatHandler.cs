using Hedron.Commands;
using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Item;
using Hedron.Core.Entity.Living;
using Hedron.Core.Entity.Property;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Skills;
using Hedron.Skills.Passive;
using Hedron.System;
using Hedron.System.Text;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Razor.Language;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Combat
{
    public static class CombatHandler
	{
		/// <summary>
		/// Map entity combat targets. Each entity may have 1 target.
		/// </summary>
		private static Dictionary<uint?, uint?> CombatTargets { get; set; } = new Dictionary<uint?, uint?>();

		/// <summary>
		/// Enters entity into combat with another entity
		/// </summary>
		/// <param name="entityID">The source entity ID</param>
		/// <param name="targetID">The target entity ID</param>
		/// <param name="forceTargetChange">Whether to change force the target to retarget the source</param>
		public static void Enter(uint? entityID, uint? targetID, bool forceTargetChange)
		{
			var entity = DataAccess.Get<EntityAnimate>(entityID, CacheType.Instance);
			var target = DataAccess.Get<EntityAnimate>(targetID, CacheType.Instance);

			if (entity == null || target == null)
				return;

			entity.StateHandler.State = Network.GameState.Combat;
			target.StateHandler.State = Network.GameState.Combat;
			
			CombatTargets[entityID] = targetID;

			if (forceTargetChange || !CombatTargets.ContainsKey(targetID))
				CombatTargets[targetID] = entityID;
		}

		/// <summary>
		/// Exit entity from combat and handle related combatants
		/// </summary>
		/// <param name="entityID">The entity to remove from combat</param>
		/// <remarks>If any other entities are involved in the combat, they will be removed if there are no other valid targets,
		/// otherwise they will be retargeted to an appropriate entity.</remarks>
		public static void Exit(uint? entityID)
		{
			var entity = DataAccess.Get<EntityAnimate>(entityID, CacheType.Instance);

			if (entity == null || !CombatTargets.ContainsKey(entityID))
				return;

			var entitiesTargetingExiter = GetEntitiesTargeting(entityID);

			entity.StateHandler.State = Network.GameState.Active;
			CombatTargets.Remove(entityID);

			foreach (var targeter in entitiesTargetingExiter)
			{
				var entitiesTargetingTarget = GetEntitiesTargeting(targeter);

				if (entitiesTargetingTarget.Count == 0)
				{
					// Remove targeter from combat
					var instanceTargeter = DataAccess.Get<EntityAnimate>(targeter, CacheType.Instance);

					if (instanceTargeter != null)
						instanceTargeter.StateHandler.State = Network.GameState.Active;

					CombatTargets.Remove(targeter);
				}
				else
				{
					// Set targeter to target the first entity attacking them
					CombatTargets[targeter] = entitiesTargetingTarget[0];
				}
			}
		}
		
		/// <summary>
		/// Get a list of the entities targeting this entity
		/// </summary>
		/// <param name="entityID">The entity being targeted</param>
		/// <returns>The entities targeting the given entity</returns>
		public static List<uint?> GetEntitiesTargeting(uint? entityID)
		{
			return CombatTargets.Where(kvp => kvp.Value == entityID).Select(kvp => kvp.Key).ToList();
		}

		/// <summary>
		/// Gets the target of a given entity
		/// </summary>
		/// <param name="entityID">The entity in combat</param>
		/// <returns>The given entity's combat target</returns>
		public static uint? GetTarget(uint? entityID)
		{
			if (CombatTargets.ContainsKey(entityID))
				return CombatTargets[entityID];
			else
				return null;
		}

		/// <summary>
		/// Process combat for all fighting entities
		/// </summary>
		public static void ProcessAllEntityCombatRound()
		{
			// Skip if nothing is in combat
			if (CombatTargets.Count == 0)
				return;

			// Process attacks for players first
			var players = DataAccess.GetMany<Player>(CombatTargets.Keys.Cast<uint>().ToList(), CacheType.Instance);
			foreach (var p in players)
				ProcessEntityAutoAttack(p, DataAccess.Get<EntityAnimate>(CombatTargets[p.Instance], CacheType.Instance));
			
			// Process attacks for mobs next
			var mobs = DataAccess.GetMany<Mob>(CombatTargets.Keys.Cast<uint>().ToList(), CacheType.Instance);
			foreach (var m in mobs)
				ProcessEntityAutoAttack(m, DataAccess.Get<EntityAnimate>(CombatTargets[m.Instance], CacheType.Instance));

			foreach (var p in players)
			{
				p.IOHandler.QueueOutput("\n" + p.GetParsedPrompt());
				p.IOHandler.SendOutput();
			}
		}
		
		/// <summary>
		/// Process an entity's auto attack
		/// </summary>
		/// <param name="numAttacks">The number of attacks to process</param>
		/// <param name="source">The attacking entity</param>
		/// <param name="target">The target entity</param>
		public static void ProcessEntityAutoAttack(EntityAnimate source, EntityAnimate target)
		{
			if (source == null || target == null)
				return;

			var rand = World.Random;
			var attackRating = 0;
			var weapons = source.GetItemsEquippedAt(ItemSlot.OneHandedWeapon, ItemSlot.TwoHandedWeapon).Cast<ItemWeapon>().ToList();
			var skillResults = new List<ImproveSkillResult>();
			string supportSkillToImprove = "";

			// Calculate the number of attacks
			var numAttacks = CombatHelper.CalcEntityMaxNumAttacks(source);
			var attackChanceList = new List<double>();

			// Protect unknown circumstances
			if (numAttacks < 1)
			{
				Logger.Info(nameof(CombatHandler), nameof(ProcessEntityAutoAttack), $"Number of attacks for {source.Name}: ID {source.Prototype} was less than 1.");
				string sourceShort = source.ShortDescription ?? "";
				string targetShort = target.ShortDescription.FirstLetterToUpperCaseOrConvertNullToEmptyString();

				source.IOHandler?.QueueOutput($"You struggle to attack {targetShort}, but can't!");
				target.IOHandler?.QueueOutput($"{sourceShort} struggles to attack you, but can't!");
				return;
			}

			// Get support skill for later improvement
			if (source.GetType() == typeof(Player))
			{
				if (weapons.Count == 0)
				{
					// Unarmed
					supportSkillToImprove = SkillMap.SkillToFriendlyName(typeof(Unarmed));
				}
				else if (weapons.Count == 1)
				{
					// Two handed?
					if (weapons[0].Slot == ItemSlot.TwoHandedWeapon)
						supportSkillToImprove = SkillMap.SkillToFriendlyName(typeof(TwoHanded));
					else
						supportSkillToImprove = SkillMap.SkillToFriendlyName(typeof(OneHanded));
				}
				else
				{
					// Dual wielding
					supportSkillToImprove = SkillMap.SkillToFriendlyName(typeof(DualWield));
				}
			}

			// Build attack chance list
			for (int i = 0; i < numAttacks; i++)
				attackChanceList.Add(CombatHelper.CalculateAttackChance(attackRating, i));

			// Iterate attack chance list and process attacks
			for (int i = 0; i < attackChanceList.Count; i++)
			{
				int damage = 0;
				bool didHit = false;

				// Improve support skills
				if (source.GetType() == typeof(Player))
					skillResults.Add(source.ImproveSkill(supportSkillToImprove, attackChanceList[i]));

				// Process the attack if it succeeds and handle skill improvement
				if (rand.NextDouble() <= attackChanceList[i])
				{
					didHit = true;

					if (weapons.Count == 0)
					{
						// Unarmed damage
						damage = (source.Tier.Level * 2) + (int)source.ModifiedAttributes.Might * 3 / 5;
						damage = rand.Next((int)Math.Floor(damage * 0.9), (int)Math.Ceiling(damage * 1.1));
						if (source.GetType() == typeof(Player))
							skillResults.Add(source.ImproveSkill(SkillMap.SkillToFriendlyName(typeof(Unarmed)), 1));
					}
					else if (weapons.Count == 1)
					{
						// Single weapon damage
						if (weapons[0].Slot == ItemSlot.TwoHandedWeapon)
						{
							damage = rand.Next(weapons[0].MinDamage, weapons[0].MaxDamage + 1) + ((int)source.ModifiedAttributes.Might / 3);
						}
						else
						{
							damage = rand.Next(weapons[0].MinDamage, weapons[0].MaxDamage + 1) + ((int)source.ModifiedAttributes.Might / 5);
						}

						if (source.GetType() == typeof(Player))
							skillResults.Add(source.ImproveSkill(SkillMap.WeaponTypeToSkillName(weapons[0].WeaponType), 1));
					}
					else
					{
						// Dual wield; alternate weapons for each attack
						int weaponIndex = i % 2;
						damage = rand.Next(weapons[weaponIndex].MinDamage, weapons[0].MaxDamage + 1) + ((int)source.ModifiedAttributes.Might / 5);
						if (source.GetType() == typeof(Player))
							skillResults.Add(source.ImproveSkill(SkillMap.WeaponTypeToSkillName(weapons[weaponIndex].WeaponType), 1));
					}
				}

				if (didHit)
				{
					if (damage < 1)
						damage = 1;

					var crit = rand.NextDouble() * 100 <= source.ModifiedQualities.CriticalHit;

					if (crit)
						damage = (int)(damage + (damage * source.ModifiedQualities.CriticalDamage / 100));

					var (hitPoints, died) = target.ModifyCurrentHealth(0 - damage, true);

					source.IOHandler?.QueueOutput($"You{(crit ? " critically" : "")} hit {target.ShortDescription} for {damage} damage{(crit ? "!" : ".")} ({target.CurrentHitPoints}/{target.ModifiedPools.HitPoints})");
					target.IOHandler?.QueueOutput($"{source.ShortDescription}{(crit ? " critically" : "")} hits you for {damage}{(crit ? "!" : ".")}");

					// Handle target death
					if (died)
					{
						Exit(target.Instance);

						if (target.GetType() == typeof(Mob))
							DataAccess.Remove<Mob>(target.Instance, CacheType.Instance);

						if (target.GetType() == typeof(Player))
						{
							target.IOHandler.QueueOutput("You have died!");

							var args = new Commands.CommandEventArgs(
								DataAccess.GetAll<World>(CacheType.Instance)?[0].Instance.ToString(),
								target,
								null);

							new Commands.Movement.Goto().Execute(args);
						}

						source.IOHandler?.QueueOutput($"You have slain {target.ShortDescription}!");

						break;
					}
				}
			}

			// Handle skill improvement messages
			// TODO: Add configuration options to allow for suppressions skill improvement messages.
			foreach (var result in skillResults)
			{
				source.IOHandler?.QueueOutput(result.ImprovedMessage);
			}
		}
	}
}
