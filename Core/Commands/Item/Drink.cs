using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Item;
using Hedron.Core.Entities.Properties;
using Hedron.Data;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Commands.Item
{
	/// <summary>
	/// Drinks a potion or from a consumable
	/// </summary>
	public class Drink : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Drink()
		{
			FriendlyName = "drink";
			PrivilegeLevel = PrivilegeLevel.NPC;
			ValidStates.Add(EntityState.Active);
			ValidStates.Add(EntityState.Combat);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			try
			{
				base.Execute(commandEventArgs);
			}
			catch (CommandException ex)
			{
				return ex.CommandResult;
			}

			var nameToDrink = CommandService.ParseFirstArgument(commandEventArgs.Argument).ToUpper();

			if (nameToDrink == "")
				return CommandResult.InvalidSyntax(nameof(Drink), new List<string> { "potion name" });

			// Search inventory for a match
			var inventoryEntities = commandEventArgs.Entity.GetInventoryItems().Where(i => i.GetType() == typeof(ItemPotion)).ToList();

			if (inventoryEntities.Count == 0)
			{
				return new CommandResult(ResultCode.FAIL, "You don't have anything to drink!");
			}

			var output = new OutputBuilder();
			var itemMatched = Parse.MatchOnEntityNameByOrder(nameToDrink, inventoryEntities.Cast<IEntity>().ToList());

			if (itemMatched == null)
			{
				return CommandResult.Failure("You can't seem to find it.");
			}

			var potion = (ItemPotion)itemMatched;
			output.Append($"You drink {potion.ShortDescription}");

			if (potion.Effect != null)
			{
				commandEventArgs.Entity.AddEffect(potion.Effect, false);
				output.Append(potion.Effect.ApplyDescriptionSelf);
			}

			if (potion.PoolRestoration != null)
			{
				int energyChange = 0;
				int healthChange = 0;
				int staminaChange = 0;

				if (potion.PoolRestoration.IsMultiplier)
				{
					energyChange = (int)(commandEventArgs.Entity.ModifiedPools.Energy.GetValueOrDefault() * potion.PoolRestoration.Energy.GetValueOrDefault());
					healthChange = (int)(commandEventArgs.Entity.ModifiedPools.HitPoints.GetValueOrDefault() * potion.PoolRestoration.HitPoints.GetValueOrDefault());
					staminaChange = (int)(commandEventArgs.Entity.ModifiedPools.Stamina.GetValueOrDefault() * potion.PoolRestoration.Stamina.GetValueOrDefault());
				}
				else
				{
					energyChange = (int)(potion.PoolRestoration.Energy.GetValueOrDefault());
					healthChange = (int)(potion.PoolRestoration.HitPoints.GetValueOrDefault());
					staminaChange = (int)(potion.PoolRestoration.Stamina.GetValueOrDefault());
				}

				if (energyChange != 0)
				{
					commandEventArgs.Entity.ModifyCurrentEnergy(energyChange, false);
					output.Append(string.Format("You feel your energy {0}.", energyChange > 0 ? "recharge" : "drain"));
				}

				if (healthChange != 0)
				{
					commandEventArgs.Entity.ModifyCurrentHealth(healthChange, false);
					output.Append(string.Format("You feel your health {0}.", healthChange > 0 ? "improve" : "fade"));
				}

				if (staminaChange != 0)
				{
					commandEventArgs.Entity.ModifyCurrentStamina(staminaChange, false);
					output.Append(string.Format("You feel your stamina {0}.", staminaChange > 0 ? "refresh" : "falter"));
				}
			}

			// Remove potion from instance and prototype inventory/caches (automatically updates persistence accordingly)
			commandEventArgs.Entity.RemoveInventoryItem(potion.Instance);

			var protoEntity = DataAccess.Get<EntityAnimate>(commandEventArgs.Entity.Prototype, CacheType.Prototype);
			protoEntity?.RemoveInventoryItem(potion.Prototype);

			DataAccess.Remove<ItemPotion>(potion.Prototype, CacheType.Prototype);

			// TODO: Add entity persistence once player saving works
			// DataPersistence.SaveObject(protoEntity);

			return CommandResult.Success(output.Output);
		}
	}
}
