using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Data;
using System;
using System.Collections.Generic;

namespace Hedron.Core.Commands.Movement
{
    /// <summary>
    /// Moves an entity directly to a location
    /// </summary>
    public class Goto : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Goto()
		{
			FriendlyName = "goto";
			PrivilegeLevel = PrivilegeLevel.Builder;
			ValidStates.Add(EntityState.Active);
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

			var output = new OutputBuilder();
			var argument = commandEventArgs.Argument;
			var entity = commandEventArgs.Entity;

			if (!string.IsNullOrEmpty(argument) && entity != null)
			{
				int iRoom = -1;
				try
				{
					// Try to convert argument to guid
					iRoom = Convert.ToInt32(CommandService.ParseFirstArgument(argument));
				}
				catch (FormatException)
				{
					return CommandResult.InvalidSyntax(nameof(Goto), new List<string> { "list", "[room number]" });
				}
				catch (OverflowException)
				{
					Logger.Error(nameof(CommandService), nameof(Goto), "Overflow exception.");
				}

				var targetRoom = DataAccess.Get<Room>((uint)iRoom, CacheType.Instance);

				if (targetRoom != null)
				{
					// Move entity
					var sourceRoom = entity.GetInstanceParentRoom();

					sourceRoom?.Animates.RemoveEntity(entity.Instance, entity);
					targetRoom.Animates.AddEntity(entity.Instance, entity, false);

					output.Append(
						new Look()
						.Execute(new CommandEventArgs("", commandEventArgs.Entity, commandEventArgs.PrivilegeOverride))
						.ResultMessage);

					return CommandResult.Success(output.Output);
				}
				else
				{
					output.Append("Invalid room.");
					return CommandResult.Failure(output.Output);
				}
			}
			else
			{
				if (!string.IsNullOrEmpty(argument) && argument.ToLower() == "list")
				{
					var rooms = DataAccess.GetAll<Room>(CacheType.Instance);
					foreach (var r in rooms)
					{
						output.Append($"({r.Instance}): {r.GetInstanceParentArea().Name}/{r.Name}");
					}
					return CommandResult.Success(output.Output);
				}
				else
				{
					return CommandResult.InvalidSyntax(nameof(Goto), new List<string> {"list", "[room number]" });
				}
			}
		}
	}
}