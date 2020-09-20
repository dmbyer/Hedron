using Hedron.Core.Entities.Base;
using System;

namespace Hedron.Core.Commands
{
    public class CommandEventArgs : EventArgs
	{
		/// <summary>
		/// The command argument
		/// </summary>
		public string Argument { get; }

		/// <summary>
		/// The entity executing the command
		/// </summary>
		public EntityAnimate Entity { get; }

		/// <summary>
		/// Executes with an overriden privilege level, if provided
		/// </summary>
		public PrivilegeLevel? PrivilegeOverride { get; }

		/// <summary>
		/// Private default constructor
		/// </summary>
		private CommandEventArgs()
		{

		}

		/// <summary>
		/// Default constructor
		/// </summary>
		/// <param name="argument">The argument for the command</param>
		/// <param name="entity">The entity executing the command</param>
		/// <param name="privilegeOverride">The privilege level to use as an override for execution</param>
		public CommandEventArgs(string argument, EntityAnimate entity, PrivilegeLevel? privilegeOverride)
		{
			Argument = argument;
			Entity = entity;
			PrivilegeOverride = privilegeOverride;
		}
	}
}