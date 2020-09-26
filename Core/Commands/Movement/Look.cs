using Hedron.Core.Entities.Base;
using Hedron.Core.Container;
using Hedron.Core.Entities.Living;
using Hedron.Core.Entities.Properties;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;
using System.Collections.Generic;

namespace Hedron.Core.Commands.Movement
{
    /// <summary>
    /// Shows player information about the thing they are looking at or their surroundings
    /// </summary>
    public class Look : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Look()
		{
			FriendlyName = "look";
			ValidStates.Add(EntityState.Active);
			ValidStates.Add(EntityState.Combat);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			// TODO: Implement Look <Direction>
			try
			{
				base.Execute(commandEventArgs);
			}
			catch (CommandException ex)
			{
				return ex.CommandResult;
			}

			var output = new OutputBuilder();
			var entity = commandEventArgs.Entity;
			var argument = commandEventArgs.Argument;
			var room = entity.GetInstanceParentRoom();

			if (room != null)
			{
				var items = room.Items.GetAllEntitiesAsObjects<EntityInanimate>();
				var storage = room.StorageItems.GetAllEntitiesAsObjects<Storage>();
				var mobs = room.Animates.GetAllEntitiesAsObjects<Mob>();
				var players = room.Animates.GetAllEntitiesAsObjects<Player>();

				if (entity.GetType() == typeof(Player))
					players.Remove((Player)entity);
				else if (entity.GetType() == typeof(Mob))
					mobs.Remove((Mob)entity);

				if (argument?.Length > 0)
				{
					var allEntities = new List<IEntity>();
					allEntities.AddRange(items);
					allEntities.AddRange(storage);
					allEntities.AddRange(mobs);
					allEntities.AddRange(players);
					var matchedEntity = Parse.MatchOnEntityNameByOrder(argument, allEntities);

					if (matchedEntity != null)
						output.Append(matchedEntity.LongDescription);
					else
						return CommandResult.Failure("You do not see that here.");
				}
				else
				{

					var area = entity.GetInstanceParentArea();

					output.Append("");

					if (entity.GetType() == typeof(Player))
						output.Append(((Player)entity).Configuration.DisplayAreaName
							? $"{area.Name} > {room.Name}"
							: room.Name);

					output.Append(room.Description);

					output.Append("Exits: " + Formatter.ParseExits(room));

					// Print players
					if (players.Count > 0)
						foreach (var desc in EntityQuantityMapper.ParseEntityQuantitiesAsStrings(players, EntityQuantityMapper.MapStringTypes.ShortDescription))
							output.Append(desc);

					// Print mobs
					if (mobs.Count > 0)
						output.Append(string.Join(", ",
							EntityQuantityMapper.ParseEntityQuantitiesAsStrings(mobs, EntityQuantityMapper.MapStringTypes.ShortDescription).ToArray()));

					// Print items
					if (items.Count > 0)
						output.Append(string.Join(", ",
							EntityQuantityMapper.ParseEntityQuantitiesAsStrings(items, EntityQuantityMapper.MapStringTypes.ShortDescription).ToArray()));

					// Print containers
					if (storage.Count > 0)
						output.Append(string.Join(", ",
							EntityQuantityMapper.ParseEntityQuantitiesAsStrings(storage, EntityQuantityMapper.MapStringTypes.ShortDescription).ToArray()));
				}
			}
			else
			{
				output.Append("You are in the void...");
			}

			return CommandResult.Success(output.Output);
		}
	}
}