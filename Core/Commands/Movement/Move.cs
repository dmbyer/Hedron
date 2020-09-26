using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Living;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;

namespace Hedron.Core.Commands.Movement
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
			ValidStates.Add(EntityState.Active);
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

			Room sourceRoom = entity.GetInstanceParentRoom();

			// Don't move entity if it's not already in a room
			if (sourceRoom != null && entity != null)
			{
				RoomExits exits = sourceRoom.Exits;
				Room destRoom;

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
					sourceRoom.Animates.RemoveEntity(entity.Instance, entity);
					destRoom.Animates.AddEntity(entity.Instance, entity, false);

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
							sourceRoom = DataAccess.Get<Room>(sourceRoom.Prototype, CacheType.Prototype);

							if (sourceRoom == null)
								return CommandResult.Failure("You must be in a room with a saved prototype before you may dig.");

							var sourceArea = entity.GetInstanceParentArea();
							if (sourceArea == null)
								return CommandResult.Failure("You must be in an area before you may dig.");

							var protoArea = DataAccess.Get<Area>(sourceArea.Prototype, CacheType.Prototype);
							if (protoArea == null)
								return CommandResult.Failure("You must be in an area with a saved prototype before you may dig.");

							destRoom = Room.NewPrototype((uint)protoArea.Prototype);
							destRoom.Spawn(false, (uint)sourceArea.Instance);

							RoomExits.ConnectRoomExits(sourceRoom, destRoom, direction, true, true);

							// Now move the entity to the new room
							output.Append($"You dig {direction.ToString().ToLower()}.");
							output.Append(new MoveEntity().Execute(commandEventArgs).ResultMessage);

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
