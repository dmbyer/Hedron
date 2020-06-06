using Hedron.Core.Container;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.System;
using Hedron.System.Exceptions.Command;
using System;
using System.Collections.Generic;

namespace Hedron.Commands.Movement
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

			var output = new OutputBuilder();
			var argument = commandEventArgs.Argument;
			var entity = commandEventArgs.Entity;

			if (argument.Length > 0 && entity != null)
			{
				int iRoom = -1;
				try
				{
					// Try to convert argument to guid
					iRoom = Convert.ToInt32(CommandHandler.ParseFirstArgument(argument));
				}
				catch (FormatException)
				{
					return CommandResult.InvalidSyntax(nameof(Goto), new List<string> { "room number" });
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
				return CommandResult.InvalidSyntax(nameof(Goto), new List<string> { "room number" });
			}
		}
	}
}