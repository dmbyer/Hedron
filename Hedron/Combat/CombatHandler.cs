﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Data;
using Hedron.System;

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

			entity.StateHandler.State = Network.StateHandler.GameState.Combat;
			target.StateHandler.State = Network.StateHandler.GameState.Combat;
			
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

			entity.StateHandler.State = Network.StateHandler.GameState.Active;
			CombatTargets.Remove(entityID);

			foreach (var targeter in entitiesTargetingExiter)
			{
				var entitiesTargetingTarget = GetEntitiesTargeting(targeter);

				if (entitiesTargetingTarget.Count == 0)
				{
					// Remove targeter from combat
					var instanceTargeter = DataAccess.Get<EntityAnimate>(targeter, CacheType.Instance);

					if (instanceTargeter != null)
						instanceTargeter.StateHandler.State = Network.StateHandler.GameState.Active;

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
		/// <param name="entityID"></param>
		/// <returns></returns>
		public static List<uint?> GetEntitiesTargeting(uint? entityID)
		{
			return CombatTargets.Where(kvp => kvp.Value == entityID).Select(kvp => kvp.Key).ToList();
		}

		/// <summary>
		/// Process combat for all fighting entities
		/// </summary>
		public static void ProcessAllEntityCombatRound()
		{
			// Process attacks for players first
			var players = DataAccess.GetMany<Player>(CombatTargets.Keys.Cast<uint>().ToList(), CacheType.Instance);
			foreach (var p in players)
				ProcessEntityAutoAttack(1, p, DataAccess.Get<EntityAnimate>(CombatTargets[p.Instance], CacheType.Instance));
			
			// Process attacks for mobs next
			var mobs = DataAccess.GetMany<Mob>(CombatTargets.Keys.Cast<uint>().ToList(), CacheType.Instance);
			foreach (var m in mobs)
				ProcessEntityAutoAttack(1, m, DataAccess.Get<EntityAnimate>(CombatTargets[m.Instance], CacheType.Instance));

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
		public static void ProcessEntityAutoAttack(int numAttacks, EntityAnimate source, EntityAnimate target)
		{
			if (numAttacks == 0 || source == null || target == null)
				return;

			var rand = new Random();
			var weaponDamage = rand.Next(source.Tier.Level, source.Tier.Level * 2);
			var weapons = source.EquippedAt(ItemSlot.OneHandedWeapon, ItemSlot.TwoHandedWeapon);

			if (weapons.Count != 0)
			{
				var weaponToUse = (ItemWeapon)weapons[rand.Next(weapons.Count)];
				if (weaponToUse.MinDamage <= weaponToUse.MaxDamage && weaponToUse.MinDamage > 0)
					weaponDamage = rand.Next(weaponToUse.MinDamage, weaponToUse.MaxDamage);
			}

			// var attack = source.BaseQualities.AttackRating;
			var defense = target.BaseQualities.ArmorRating;
			var damage = weaponDamage * weaponDamage / (weaponDamage > 0 || defense > 0 ? (weaponDamage + defense) : 1);

			if (damage < 1)
				damage = 1;

			var crit = rand.Next(1, 101) <= source.BaseQualities.CriticalHit ? true : false;

			if (crit)
				damage = damage + (damage * source.BaseQualities.CriticalDamage / 100);

			target.BaseAspects.CurrentHitPoints -= (int)damage;

			source.IOHandler?.QueueOutput($"You hit {target.ShortDescription} for {damage.ToString()} damage.");
			target.IOHandler?.QueueOutput($"{source.ShortDescription} hits you for {damage.ToString()}.");

			// Handle target death
			if (target.BaseAspects.CurrentHitPoints <= 0)
			{
				var corpse = Corpse.CreateCorpse(target.Instance);
				var parentRoom = EntityContainer.GetInstanceParent<Room>(target.Instance);

				parentRoom?.AddEntity(corpse.Instance, corpse);
				Exit(target.Instance);

				if (target.GetType() == typeof(Mob))
					DataAccess.Remove<Mob>(target.Instance, CacheType.Instance);

				if (target.GetType() == typeof(Player))
				{
					target.IOHandler.QueueOutput("You have died!");
					Commands.CommandHandler.InvokeCommand(Commands.Command.CMD_GOTO, DataAccess.GetAll<World>(CacheType.Instance)?[0].Instance.ToString(), target);
				}

				source.IOHandler?.QueueOutput($"You have slain {target.ShortDescription}!");
			}
		}
	}
}