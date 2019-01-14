using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Data;
using Hedron.System;
using Hedron.System.Exceptions;

namespace Hedron.Commands.Movement
{
	/// <summary>
	/// Moves an entity from one location to another
	/// </summary>
	public class MoveEntity : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public MoveEntity()
		{
			FriendlyName = "move entity";
			PrivilegeLevel = PrivilegeLevel.NPC;
			ValidStates.Add(Network.GameState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			return CommandResult.Failure($"{FriendlyName} must be executed with a direction argument.");
		}

		public CommandResult Execute(CommandEventArgs commandEventArgs, Constants.EXIT direction)
		{
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
						output.Append("You cannot go that way.");
						return CommandResult.Failure(output.Output);
				}

				if (destRoom != null)
				{
					sourceRoom.RemoveEntity(entity.Instance, entity);
					destRoom.AddEntity(entity.Instance, entity);

					output.Append(
						new Look()
						.Execute(new CommandEventArgs("", commandEventArgs.Entity, commandEventArgs.PrivilegeOverride))
						.ResultMessage);

					return CommandResult.Success(output.Output);
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

							RoomExits.ConnectRoomExits(sourceRoom, destRoom, direction, true, true);

							// Immediately save changes
							DataPersistence.SaveObject(DataAccess.Get<Room>(sourceRoom.Prototype, CacheType.Prototype));
							DataPersistence.SaveObject(destRoom);

							output.Append($"You dig {direction.ToString().ToLower()}.");
							output.Append(new MoveEntity().Execute(commandEventArgs).ResultMessage);

							// Now move the entity to the new room
							return CommandResult.Success(output.Output);
						}
					}

					output.Append("You cannot go that way.");
					return CommandResult.Failure(output.Output);
				}
			}

			// Return failure if entity was not already in a room or there was no entity to move
			output.Append("You cannot go that way.");
			return CommandResult.Failure(output.Output);
		}
	}
}
