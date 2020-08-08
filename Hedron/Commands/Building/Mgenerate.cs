using Hedron.Combat;
using Hedron.Core.Container;
using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Living;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.System;
using Hedron.System.Autogeneration;
using Hedron.System.Exceptions.Command;
using Hedron.System.Text;
using System.Linq;
using System.Collections.Generic;

namespace Hedron.Commands.Building
{
	/// <summary>
	/// Lists entities
	/// </summary>
	public class Mgenerate : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Mgenerate()
		{
			FriendlyName = "mgenerate";
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

			var name = commandEventArgs.Argument;
			var room = EntityContainer.GetInstanceParent<Room>(commandEventArgs.Entity.Instance);
			
			// Argument checking to ensure a name was provided
			if (name == null || name == "")
			{
				return CommandResult.InvalidSyntax($"{nameof(Mgenerate)}", "Please enter the base name of the mob you wish to create." ,new List<string>() { "name" });
			}

			// Ensure the player is in a room so the mobs can be created here
			if (room == null)
			{
				return CommandResult.Failure("You must be in a room to execute this command.");
			}

			// Create new mobs of all levels and instantiate them in this room
			var mobs = AutogenMob.CreateAndInstantiateAllLevels(name, room.Instance);

			var output = new OutputBuilder();

			foreach (var mob in mobs)
            {
				output.Append($"You have created {DataAccess.Get<Mob>(mob, CacheType.Instance).ShortDescription}");
            }

			return CommandResult.Success(output.Output);
		}
	}
}