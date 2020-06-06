using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Item;
using Hedron.Core.Entity.Living;
using Hedron.Core.Entity.Property;
using Hedron.Core.Locale;
using Hedron.Data;
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
			// Skip if nothing is in combat
			if (CombatTargets.Count == 0)
				return;

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
			if (source == null || target == null)
				return;

			for (var i = 0; i < numAttacks && numAttacks > 0; i++)
			{
				var rand = new Random();
				var weaponDamage = source.GetType() == typeof(Player)
					? rand.Next(source.Tier.Level + (int)source.ModifiedAttributes.Might / 2, (source.Tier.Level + (int)source.ModifiedAttributes.Might / 2) * 2)
					: rand.Next(source.Tier.Level, source.Tier.Level * 2);
				var weapons = source.GetItemsEquippedAt(ItemSlot.OneHandedWeapon, ItemSlot.TwoHandedWeapon);

				if (weapons.Count != 0)
				{
					var weaponToUse = (ItemWeapon)weapons[rand.Next(weapons.Count)];
					if (weaponToUse.MinDamage <= weaponToUse.MaxDamage && weaponToUse.MinDamage > 0)
						weaponDamage = rand.Next(weaponToUse.MinDamage, weaponToUse.MaxDamage);
				}

				var defense = target.ModifiedQualities.ArmorRating;
				int damage = (int)(weaponDamage * weaponDamage / (weaponDamage > 0 || defense > 0 ? (weaponDamage + defense) : 1));

				if (damage < 1)
					damage = 1;

				var crit = rand.Next(1, 101) <= source.ModifiedQualities.CriticalHit ? true : false;

				if (crit)
					damage = (int)(damage + (damage * source.ModifiedQualities.CriticalDamage / 100));

				var status = target.ModifyCurrentHealth(0 - damage, true);

				source.IOHandler?.QueueOutput($"You hit {target.ShortDescription} for {damage.ToString()} damage. ({target.CurrentHitPoints}/{target.ModifiedPools.HitPoints})");
				target.IOHandler?.QueueOutput($"{source.ShortDescription} hits you for {damage.ToString()}.");

				// Handle target death
				if (status.Died)
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
					return;
				}
			}
		}
	}
}