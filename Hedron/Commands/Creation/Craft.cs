using Hedron.Core.Container;
using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Item;
using Hedron.Core.Entity.Property;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.System;
using Hedron.System.Exceptions.Command;
using Hedron.System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Commands.Creation
{
	public class Craft : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Craft()
		{
			FriendlyName = "craft";
			PrivilegeLevel = PrivilegeLevel.NPC;
			ValidStates.Add(Network.GameState.Active);
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

			string arg = CommandHandler.ParseFirstArgument(commandEventArgs.Argument).ToUpper();
			string opt = CommandHandler.ParseFirstArgument(CommandHandler.ParseArgument(commandEventArgs.Argument)).ToUpper();
			OutputBuilder output = new OutputBuilder();
			var entity = commandEventArgs.Entity;

			switch (arg)
			{
				case "POTION":
					ItemPotion newPotion;

					if (opt == "ENERGY")
					{
						newPotion = ItemPotion.NewInstance(true);
						newPotion.PoolRestoration = new Core.Entity.Property.Pools
						{
							Energy = 50
						};

						newPotion.Name = "energy potion";
						newPotion.ShortDescription = "an energy potion";
						newPotion.LongDescription = "A lovely blue energy potion.";
					}
					else if (opt == "HEALTH")
					{
						newPotion = ItemPotion.NewInstance(true);
						newPotion.PoolRestoration = new Core.Entity.Property.Pools
						{
							HitPoints = 50
						};

						newPotion.Name = "health potion";
						newPotion.ShortDescription = "a health potion";
						newPotion.LongDescription = "A lovely red health potion.";
					}
					else if (opt == "STAMINA")
					{
						newPotion = ItemPotion.NewInstance(true);
						newPotion.PoolRestoration = new Core.Entity.Property.Pools
						{
							Stamina = 50
						};

						newPotion.Name = "stamina potion";
						newPotion.ShortDescription = "a stamina potion";
						newPotion.LongDescription = "A lovely green stamina potion.";
					}
					else
					{
						return CommandResult.InvalidSyntax("craft potion", new List<string> { "energy", "health", "stamina"});
					}

					output.Append($"You craft {newPotion.ShortDescription}.");

					// Add potion to inventory, save potion
					entity.AddInventoryItem(newPotion.Instance);
					var protoPotion = DataAccess.Get<ItemPotion>(newPotion.Prototype, CacheType.Prototype);
					newPotion.CopyTo(protoPotion);

					// TODO: Update data persistence once player saving works
					// var entityProto = DataAccess.Get<EntityAnimate>(entity.Prototype, CacheType.Prototype);
					// entityProto?.AddInventoryItem(newPotion.Prototype);
					// DataPersistence.SaveObject(entityProto);
					DataPersistence.SaveObject(DataAccess.Get<ItemPotion>(newPotion.Prototype, CacheType.Prototype));
					break;
				default:
					return CommandResult.InvalidSyntax("craft", new List<string> { "potion" });
			}

			return CommandResult.Success(output.Output);
		}
	}
}
