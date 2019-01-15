using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Data;
using Hedron.System;
using Hedron.System.Exceptions;
using Hedron.System.Text;

namespace Hedron.Commands.Movement
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
			ValidStates.Add(Network.GameState.Active);
			ValidStates.Add(Network.GameState.Combat);
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
			Room room = EntityContainer.GetInstanceParent<Room>(entity.Instance);

			if (room != null)
			{
				if (argument?.Length > 0)
				{
					var entities = DataAccess.GetMany<Entity>(room.GetAllEntities(), CacheType.Instance)
						.Where(e => e?.Instance != entity.Instance)
						.OrderBy(e => e?.Name);

					bool found = false;

					foreach (var ent in entities)
					{
						if (ent.Name.StartsWith(argument))
						{
							output.Append(ent.LongDescription);
							found = true;
							break;
						}
					}

					if (!found)
						return CommandResult.Failure("You do not see that here.");
				}
				else
				{

					var area = EntityContainer.GetInstanceParent<Area>(room.Instance);

					output.Append("");

					if (entity.GetType() == typeof(Player))
						output.Append(((Player)entity).Configuration.DisplayAreaName
							? $"{area.Name} > {room.Name}"
							: room.Name);

					output.Append(room.Description);

					output.Append("Exits: " + Formatter.ParseExits(room));

					var roomAllEntities = room.GetAllEntities();

					var items = DataAccess.GetMany<EntityInanimate>(roomAllEntities, CacheType.Instance);
					var containers = DataAccess.GetMany<EntityContainer>(roomAllEntities, CacheType.Instance).Cast<IEntity>().ToList();
					var mobs = DataAccess.GetMany<Mob>(roomAllEntities, CacheType.Instance);
					var players = DataAccess.GetMany<Player>(roomAllEntities, CacheType.Instance);

					players.Remove((Player)entity);

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
					if (containers.Count > 0)
						output.Append(string.Join(", ",
							EntityQuantityMapper.ParseEntityQuantitiesAsStrings(containers, EntityQuantityMapper.MapStringTypes.ShortDescription).ToArray()));
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