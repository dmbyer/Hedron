﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;
using Hedron.System.Exceptions;

namespace Hedron.Commands.Building
{
	/// <summary>
	/// Lists entities
	/// </summary>
	public class Elist : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Elist()
		{
			FriendlyName = "elist";
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

			return CommandResult.NotImplemented(nameof(Elist));
		}
	}
}