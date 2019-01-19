using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;

namespace Hedron.Commands
{
	public class CommandResult
	{
		public ResultCode ResultCode { get; protected set; }
		public string ResultMessage { get; protected set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		private CommandResult()
		{

		}
		
		/// <summary>
		/// CommandResult Constructor
		/// </summary>
		/// <param name="resultCode">The result code</param>
		/// <param name="resultMessage">The result message</param>
		public CommandResult(ResultCode resultCode, string resultMessage)
		{
			ResultCode = resultCode;
			ResultMessage = resultMessage;
		}

		/// <summary>
		/// Indicates the command executed successfully.
		/// </summary>
		/// <param name="message">The success message</param>
		/// <returns>Success command result</returns>
		public static CommandResult Success(string message)
		{
			return new CommandResult { ResultCode = ResultCode.SUCCESS, ResultMessage = message };
		}

		/// <summary>
		/// Indicates a command failure.
		/// </summary>
		/// <param name="message">The failure message</param>
		/// <returns>Fail command result</returns>
		public static CommandResult Failure(string message)
		{
			return new CommandResult { ResultCode = ResultCode.FAIL, ResultMessage = message };
		}

		/// <summary>
		/// Indicates the command is not yet fully implemented.
		/// </summary>
		/// <param name="command">The name of the command not yet implemented.</param>
		/// <returns>Not Implemented command result</returns>
		public static CommandResult NotImplemented(string command)
		{
			return new CommandResult { ResultCode = ResultCode.NOT_IMPLEMENTED, ResultMessage = $"The '{command.ToLower()}' command is not yet implemented." };
		}

		/// <summary>
		/// Indicates the command was not found.
		/// </summary>
		/// <param name="command">The name of the command that was not found.</param>
		/// <returns>Not Found command result</returns>
		public static CommandResult NotFound(string command)
		{
			return new CommandResult { ResultCode = ResultCode.NOT_FOUND, ResultMessage = $"Command '{command}' not found." };
		}

		/// <summary>
		/// Indicates the command syntax is invalid.
		/// </summary>
		/// <param name="command">The name of the command which invalid syntax was provided.</param>
		/// <param name="validArgs">Valid arguments for the command. If a single list has more than one item, it will be displayed as x/y/z.</param>
		/// <returns>Invalid Syntax command result</returns>
		public static CommandResult InvalidSyntax(string command, params List<string>[] validArgs)
		{
			return InvalidSyntax(command, null, validArgs);
		}

		/// <summary>
		/// Indicates the command syntax is invalid.
		/// </summary>
		/// <param name="command">The name of the command which invalid syntax was provided.</param>
		/// <param name="validArgs">Valid arguments for the command. If a single list has more than one item, it will be displayed as x/y/z.</param>
		/// <returns>Invalid Syntax command result</returns>
		public static CommandResult InvalidSyntax(string command, string addedHelp, params List<string>[] validArgs)
		{
			var result = new CommandResult
			{
				ResultCode = ResultCode.ERR_SYNTAX,
				ResultMessage = $"Invalid syntax. Usage:\n{command.ToLower()}"
			};

			foreach (var arg in validArgs)
				result.ResultMessage += $" <{string.Join("/", arg).ToLower()}>";

			if (addedHelp != null)
				result.ResultMessage += $"\n{addedHelp}";

			return result;
		}

		/// <summary>
		/// Indicates there was a null entity passed to the command.
		/// </summary>
		/// <returns>Invalid Entity command result</returns>
		public static CommandResult NullEntity()
		{
			return new CommandResult { ResultCode = ResultCode.INVALID_ENTITY, ResultMessage = $"There is nothing to process a command for." };
		}

		/// <summary>
		/// Indicates the command can only be ran by a player
		/// </summary>
		/// <returns>Invalid Entity command result</returns>
		public static CommandResult PlayerOnly()
		{
			return new CommandResult { ResultCode = ResultCode.INVALID_ENTITY, ResultMessage = $"This is a player-only command." };
		}

		/// <summary>
		/// Indicates the command can only be ran by a player
		/// </summary>
		/// <returns>Invalid Entity command result</returns>
		public static CommandResult PrivilegeLevel(string message)
		{
			return new CommandResult { ResultCode = ResultCode.PRIVILEGE_LEVEL, ResultMessage = message };
		}
	}
}