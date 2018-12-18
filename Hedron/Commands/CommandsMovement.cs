using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;
using Hedron.Data;

namespace Hedron.Commands
{
	public static partial class CommandHandler
	{
		/// <summary>
		/// Moves the entity north
		/// </summary>
		private static CommandResult North(string argument, EntityAnimate entity)
		{
			return EntityMove(argument, entity, Constants.EXIT.NORTH);
		}

		/// <summary>
		/// Moves the entity east
		/// </summary>
		private static CommandResult East(string argument, EntityAnimate entity)
		{
			return EntityMove(argument, entity, Constants.EXIT.EAST);
		}

		/// <summary>
		/// Moves the entity south
		/// </summary>
		private static CommandResult South(string argument, EntityAnimate entity)
		{
			return EntityMove(argument, entity, Constants.EXIT.SOUTH);
		}

		/// <summary>
		/// Moves the entity west
		/// </summary>
		private static CommandResult West(string argument, EntityAnimate entity)
		{
			return EntityMove(argument, entity, Constants.EXIT.WEST);
		}

		/// <summary>
		/// Moves the entity up
		/// </summary>
		private static CommandResult Up(string argument, EntityAnimate entity)
		{
			return EntityMove(argument, entity, Constants.EXIT.UP);
		}

		/// <summary>
		/// Moves the entity down
		/// </summary>
		private static CommandResult Down(string argument, EntityAnimate entity)
		{
			return EntityMove(argument, entity, Constants.EXIT.DOWN);
		}

		/// <summary>
		/// Shows player information about the thing they are looking at
		/// </summary>
		private static CommandResult Look(string argument, EntityAnimate entity)
		{
			// TODO: Implement Look <Direction>
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Look), ex.Message);
				return CommandResult.CMD_R_FAIL;
			}

			// Player-only command to view current room.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }

			Room room = EntityContainer.GetInstanceParent<Room>(entity.Instance);

			if (room != null)
			{
				var area = EntityContainer.GetInstanceParent<Area>(room.Instance);

				entity.IOHandler.QueueOutput("");

				entity.IOHandler.QueueOutput(((Player)entity).Configuration.DisplayAreaName
					? $"{area.Name} > {room.Name}"
					: room.Name);

				entity.IOHandler.QueueOutput(room.Description);

				entity.IOHandler.QueueOutput("Exits: " + ParseExits(room));

				var roomAllEntities = room.GetAllEntities();

				var items = DataAccess.GetMany<EntityInanimate>(roomAllEntities, CacheType.Instance);
				var mobs = DataAccess.GetMany<Mob>(roomAllEntities, CacheType.Instance);
				var players = DataAccess.GetMany<Player>(roomAllEntities, CacheType.Instance);

				players.Remove((Player)entity);

				// Print players
				foreach (var desc in EntityQuantityMapper.ParseEntityQuantitiesAsStrings(players, EntityQuantityMapper.MapStringTypes.ShortDescription))
					entity.IOHandler.QueueOutput(desc);

				// Print mobs
				entity.IOHandler.QueueOutput(string.Join(", ",
					EntityQuantityMapper.ParseEntityQuantitiesAsStrings(mobs, EntityQuantityMapper.MapStringTypes.ShortDescription).ToArray()));

				// Print items
				entity.IOHandler.QueueOutput(string.Join(", ",
					EntityQuantityMapper.ParseEntityQuantitiesAsStrings(items, EntityQuantityMapper.MapStringTypes.ShortDescription).ToArray()));
			}
			else
			{
				entity.IOHandler.QueueOutput("You are in the void...");
			}
			return CommandResult.CMD_R_SUCCESS;
		}

		/// <summary>
		/// Moves an entity from one location to another
		/// </summary>
		private static CommandResult EntityMove(string argument, EntityAnimate entity, Constants.EXIT direction)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(EntityMove), ex.Message);
				return CommandResult.CMD_R_FAIL;
			}

			Room sourceRoom = EntityContainer.GetInstanceParent<Room>(entity.Instance);

			// Don't move entity if it's not already in a room
			if (sourceRoom != null && entity != null)
			{
				RoomExits exits = sourceRoom.Exits;
				Room destRoom = null;

				switch (direction)
				{
					case Constants.EXIT.NORTH:
						destRoom = DataAccess.Get<Room>(exits.North, CacheType.Instance);
						break;
					case Constants.EXIT.EAST:
						destRoom = DataAccess.Get<Room>(exits.East, CacheType.Instance);
						break;
					case Constants.EXIT.SOUTH:
						destRoom = DataAccess.Get<Room>(exits.South, CacheType.Instance);
						break;
					case Constants.EXIT.WEST:
						destRoom = DataAccess.Get<Room>(exits.West, CacheType.Instance);
						break;
					case Constants.EXIT.UP:
						destRoom = DataAccess.Get<Room>(exits.Up, CacheType.Instance);
						break;
					case Constants.EXIT.DOWN:
						destRoom = DataAccess.Get<Room>(exits.Down, CacheType.Instance);
						break;
					default:
						entity.IOHandler?.QueueOutput("You cannot go that way.");
						return CommandResult.CMD_R_SUCCESS;
				}

				if (destRoom != null)
				{
					sourceRoom.RemoveEntity(entity.Instance, entity);
					destRoom.AddEntity(entity.Instance, entity);
					Look("", entity);
					return CommandResult.CMD_R_SUCCESS;
				}
				else
				{
					// Create the room if Autodig is set
					if (entity.GetType() == typeof(Player))
					{
						var player = (Player)entity;

						if (player.Configuration.Autodig)
						{
							destRoom = Room.NewPrototype();
							var sourceArea = EntityContainer.GetInstanceParent<Area>(sourceRoom.Instance);

							destRoom.Spawn(false, sourceArea.Instance);

							sourceRoom = DataAccess.Get<Room>(sourceRoom.Prototype, CacheType.Prototype);
							DataAccess.Get<Area>(sourceArea.Prototype, CacheType.Prototype).AddEntity(destRoom.Prototype, destRoom);

							RoomExits.ConnectRoomExits(sourceRoom, destRoom, direction, true);

							// Immediately save changes
							DataPersistence.SaveObject(DataAccess.Get<Room>(sourceRoom.Prototype, CacheType.Prototype));
							DataPersistence.SaveObject(destRoom);

							entity.IOHandler.QueueOutput($"You dig {direction.ToString().ToLower()}.");

							// Now move the entity to the new room
							return EntityMove(argument, entity, direction);
						}
					}

					entity.IOHandler?.QueueOutput("You cannot go that way.");
					return CommandResult.CMD_R_SUCCESS;
				}
			}

			// Return failure if entity was not already in a room or there was no entity to move
			entity.IOHandler?.QueueOutput("You cannot go that way.");
			return CommandResult.CMD_R_FAIL;
		}

		/// <summary>
		/// Moves an entity directly to a location
		/// </summary>
		private static CommandResult Goto(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Goto), ex.Message);
				return CommandResult.CMD_R_FAIL;
			}

			if (argument.Length > 0 && entity != null)
			{
				int iRoom = -1;
				try
				{
					// Try to convert argument to guid
					iRoom = Convert.ToInt32(ParseFirstArgument(argument));
				}
				catch (FormatException)
				{
					entity.IOHandler?.QueueOutput("Invalid syntax. Use:\ngoto <room number>");
					return CommandResult.CMD_R_ERRSYNTAX;
				}
				catch (OverflowException)
				{
					Logger.Error(nameof(CommandHandler), nameof(Goto), "Overflow exception.");
				}

				var targetRoom = DataAccess.Get<Room>((uint)iRoom, CacheType.Instance);

				if (targetRoom != null)
				{
					// Move entity
					var sourceRoom = EntityContainer.GetInstanceParent<Room>(entity.Instance);

					sourceRoom?.RemoveEntity(entity.Instance, entity);
					targetRoom.AddEntity(entity.Instance, entity);

					Look("", entity);
					return CommandResult.CMD_R_SUCCESS;
				}
				else
				{
					entity.IOHandler?.QueueOutput("Invalid room.");
					return CommandResult.CMD_R_FAIL;
				}
			}
			else
			{
				entity.IOHandler?.QueueOutput("Invalid syntax. Use:\ngoto <room number>");
				return CommandResult.CMD_R_ERRSYNTAX;
			}
		}
	}
}