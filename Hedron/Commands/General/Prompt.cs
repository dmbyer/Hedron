using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.System;
using Hedron.System.Exceptions;

namespace Hedron.Commands.General
{
	/// <summary>
	/// Modifies the player's prompt
	/// </summary>
	public class Prompt : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Prompt()
		{
			FriendlyName = "prompt";
			IsPlayerOnlyCommand = true;
			PrivilegeLevel = PrivilegeLevel.Player;
			ValidStates.Add(Network.GameState.Active);
			ValidStates.Add(Network.GameState.Combat);
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

			// TODO: Rewrite to accommodate network input
			/*
            string firstarg = string.Copy(ParseFirstArgument(argument));
            firstarg = firstarg.ToUpper();

            if (firstarg == "SET")
            {
                string sout = "Enter the prompt you'd like to use. Placeholders are:\n";
                sout += "$hp - Current hit points\n"
                 + "$HP - Maximum hit points\n"
                 + "$st - Current stamina\n"
                 + "$ST - Maximum stamina\n"
                 + "$en - Current energy\n"
                 + "$EN - Maximum energy\n"
                 + "Prompt: ";
                entity.IOHandler.QueueOutput(sout);
                entity.IOHandler.SendOutput();

                string newprompt = entity.IOHandler.GetInput();

                ((Player)entity).Prompt = newprompt;

                entity.IOHandler.QueueOutput("Your prompt has been set to: " + newprompt);
                return CommandResult.CMD_R_SUCCESS;
            }

            if (firstarg == "CLEAR")
            {
                ((Player)entity).Prompt = "";
                entity.IOHandler.QueueOutput("Your prompt has been cleared.");
                return CommandResult.CMD_R_SUCCESS;
            }

            entity.IOHandler.QueueOutput("Usage: prompt <action>\nValid actions are SET and CLEAR.");
            return CommandResult.CMD_R_ERRSYNTAX;
            */

			return CommandResult.NotImplemented(nameof(Prompt));
		}
	}
}
