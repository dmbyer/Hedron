using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Data;
using Hedron.System;
using Hedron.System.Exceptions;

namespace Hedron.Commands.Building
{
	/// <summary>
	/// Sets properties of things for building
	/// </summary>
	public class Set : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Set()
		{
			FriendlyName = "set";
			PrivilegeLevel = PrivilegeLevel.Builder;
			RequiresFullMatch = true;
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
			var parentRoom = EntityContainer.GetInstanceParent<Room>(commandEventArgs.Entity.Instance);

			if (parentRoom == null)
			{
				output.Append("You must be in a room to use this command.");
				return CommandResult.Failure(output.Output);
			}

			var arg = CommandHandler.ParseFirstArgument(commandEventArgs.Argument).ToUpper();
			var txt = CommandHandler.ParseArgument(commandEventArgs.Argument);

			switch (arg)
			{
				case "NAME":
					parentRoom.Name = txt;
					if (txt != "")
						output.Append($"The room name has been set to {txt}.");
					else
						output.Append("The room name has been cleared.");
					break;
				case "DESC":
					parentRoom.Description = txt;
					if (txt != "")
						output.Append("The room description has been set.");
					else
						output.Append("The room description has been cleared.");
					break;
				default:
					return CommandResult.InvalidSyntax(nameof(Set), new List<string> { "name", "desc" }, new List<string> { "new text" });
			}

			// Update prototype and persist changes
			parentRoom.CopyTo(DataAccess.Get<Room>(parentRoom.Prototype, CacheType.Prototype));
			DataPersistence.SaveObject(DataAccess.Get<Room>(parentRoom.Prototype, CacheType.Prototype));

			return CommandResult.Success(output.Output);
		}
	}
}
