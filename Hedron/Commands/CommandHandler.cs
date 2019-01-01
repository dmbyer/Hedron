using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;
using Hedron.Data;

// TODO: Implement ICommand or ICommandPartial to allow for commonality, like syntax/help.
// TODO: Return more robust command result that includes success/error code + message
//		e.g. CMD_R_ERRSYNTAX, "goto <room>"

// TODO: Create a CommandHandler for each state? This way a StateHandler can pass input/output
// according to the current state.

namespace Hedron.Commands
{
	#region Command Enums
	/// <summary>
	/// Enumeration of all the commands that can be executed
	/// </summary>
	public enum Command
    {
        CMD_NOTFOUND = -1,

        // Operational commands
        CMD_QUIT,
        CMD_HELP,
        CMD_SAVE,
		CMD_CONFIG,

        // Movement commands
        CMD_NORTH,
        CMD_EAST,
        CMD_SOUTH,
        CMD_WEST,
        CMD_UP,
        CMD_DOWN,

        CMD_LOOK,
        CMD_GOTO,

		// Item commands
		CMD_EQUIPMENT,
		CMD_GET,
		CMD_DROP,
		CMD_INVENTORY,
		CMD_REMOVE,
		CMD_WEAR,

		// General commands
		CMD_BLANK_LINE,
		CMD_PROMPT,
		CMD_STATS,

		// Building commands
		CMD_AUTODIG,
		CMD_ELIST,
		CMD_SET,

		// Combat commands
		CMD_FLEE,
		CMD_KILL,

		// System commands
		CMD_SHUTDOWN
    }
	#endregion
	
	public static partial class CommandHandler
	{
		#region Private fields
		/// <summary>
		/// Commands to be prioritized above all others when matching
		/// </summary>
		private static List<string> _commandsPriority = new List<string>()
		{
			"DOWN",
			"EAST",
			"NORTH",
			"SOUTH",
			"UP",
			"WEST"
		};

		/// <summary>
		/// Commands to be processed after priority
		/// </summary>
		private static List<string> _commandsNormal = new List<string>()
		{
			"DROP",
			"EQUIPMENT",
			"FLEE",
			"GET",
			"GOTO",
			"INVENTORY",
			"KILL",
			"LOOK",
			"PROMPT",
			"REMOVE",
			"STATS",
			"WEAR"
		};

		/// <summary>
		/// Commands requiring a full match in order to execute
		/// </summary>
		private static List<string> _commandsFullMatch = new List<string>()
		{
			"\n",
			"AUTODIG",
			"CONFIG",
			"HELP",
			"ELIST",
			"QUIT",
			"SAVE",
			"SET",
			"SHUTDOWN"
		};

		/// <summary>
		/// Maps command strings to the corresponding Command
		/// </summary>
		private static Dictionary<string, Command> _commandMap = new Dictionary<string, Command>()
		{
			// Operational commands
			{"QUIT",      Command.CMD_QUIT },
			{"HELP",      Command.CMD_HELP },
			{"SAVE",      Command.CMD_SAVE },
			{"CONFIG",    Command.CMD_CONFIG },

			// Movement commands
			{"NORTH",     Command.CMD_NORTH },
			{"EAST",      Command.CMD_EAST },
			{"SOUTH",     Command.CMD_SOUTH },
			{"WEST",      Command.CMD_WEST },
			{"UP",        Command.CMD_UP },
			{"DOWN",      Command.CMD_DOWN },
			{"LOOK",      Command.CMD_LOOK },
			{"GOTO",      Command.CMD_GOTO },

			// Item commands
			{"DROP",      Command.CMD_DROP },
			{"EQUIPMENT", Command.CMD_EQUIPMENT },
			{"GET",       Command.CMD_GET },
			{"INVENTORY", Command.CMD_INVENTORY },
			{"REMOVE",    Command.CMD_REMOVE },
			{"WEAR",      Command.CMD_WEAR },

			// General commands
			{"\n",        Command.CMD_BLANK_LINE },
			{"PROMPT",    Command.CMD_PROMPT },
			{"STATS",     Command.CMD_STATS },

			// Building commands
			{"AUTODIG",   Command.CMD_AUTODIG },
			{"ELIST",     Command.CMD_ELIST },
			{"SET",       Command.CMD_SET },

			// Combat commands
			{"FLEE",      Command.CMD_FLEE },
			{"KILL",      Command.CMD_KILL },

			// System commands
			{"SHUTDOWN",  Command.CMD_SHUTDOWN }
		};
		#endregion

		#region Process Input / Invoke Commands
		/// <summary>
		/// Process a command from network input
		/// </summary>
		/// <param name="input">The string input to parse</param>
		/// <param name="entity">The entity invoking the command</param>
		/// <returns>The result of the command</returns>
		public static CommandResult ProcessCommand(string input, EntityAnimate entity)
        {
			var command = ParseCommand(input, entity);
			if (command == Command.CMD_NOTFOUND)
				return CommandResult.NotFound(ParseFirstArgument(input).ToLower());
			
			return InvokeCommand(command, ParseArgument(input), entity);
        }

		/// <summary>
		/// Invokes a command
		/// </summary>
		/// <param name="eCmd">The command to invoke</param>
		/// <param name="argument">Any argument data to pass to the command</param>
		/// <param name="entity">The entity invoking the command</param>
		/// <returns></returns>
        public static CommandResult InvokeCommand(Command eCmd, string argument, EntityAnimate entity)
        {
            switch (eCmd)
            {
				// Operational commands
                case Command.CMD_QUIT:       return Quit(argument, entity);
                case Command.CMD_HELP:       return Help(argument, entity);
                case Command.CMD_SAVE:       return Save(argument, entity);
				case Command.CMD_CONFIG:     return Config(argument, entity);

				// Movement commands
				case Command.CMD_NORTH:      return North(argument, entity);
                case Command.CMD_EAST:       return East(argument, entity);
                case Command.CMD_SOUTH:      return South(argument, entity);
                case Command.CMD_WEST:       return West(argument, entity);
                case Command.CMD_UP:         return Up(argument, entity);
                case Command.CMD_DOWN:       return Down(argument, entity);

                case Command.CMD_LOOK:       return Look(argument, entity);
                case Command.CMD_GOTO:       return Goto(argument, entity);

				// Item commands
				case Command.CMD_DROP:       return Drop(argument, entity);
				case Command.CMD_EQUIPMENT:  return Equipment(argument, entity);
				case Command.CMD_GET:        return Get(argument, entity);
				case Command.CMD_INVENTORY:  return Inventory(argument, entity);
				case Command.CMD_REMOVE:     return Remove(argument, entity);
				case Command.CMD_WEAR:       return Wear(argument, entity);

				// General commands
				case Command.CMD_BLANK_LINE: return BlankLine(argument, entity);
				case Command.CMD_PROMPT:     return Prompt(argument, entity);
				case Command.CMD_STATS:      return Stats(argument, entity);

				// Building commands
				case Command.CMD_AUTODIG:    return Autodig(argument, entity);
				case Command.CMD_ELIST:      return EList(argument, entity);
				case Command.CMD_SET:        return Set(argument, entity);

				// Combat commands
				case Command.CMD_FLEE:       return Flee(argument, entity);
				case Command.CMD_KILL:       return Kill(argument, entity);

				// System commands
				case Command.CMD_SHUTDOWN:   return Shutdown(argument, entity);

                // Command not found
                // Change this implementation to throw an exception?
                default: return CommandResult.NotFound(nameof(eCmd));
            }
        }
		#endregion

        #region Helper methods
		/// <summary>
		/// Parses a command
		/// </summary>
		/// <param name="input">The network input to parse</param>
		/// <param name="entity">The entity requesting a command invocation</param>
		/// <returns>The command to invoke</returns>
        private static Command ParseCommand(string input, EntityAnimate entity)
		{
			if (input == null)
			{
				return Command.CMD_NOTFOUND;
			}

			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(ParseCommand), ex.Message);
				return Command.CMD_NOTFOUND;
			}

			// Prepare command string
            string toMatch = string.Copy(input);
            toMatch = toMatch.Trim();
            int i = toMatch.IndexOf(' ');
            if (i != -1)
                toMatch = toMatch.Substring(0, toMatch.IndexOf(' '));
            toMatch = toMatch.ToUpper();

			if (input == "\n")
				toMatch = "\n";

			// Match input to command
			foreach (var command in _commandsPriority)
			{
				if (command.StartsWith(toMatch))
					return _commandMap[command];
			}

			foreach (var command in _commandsNormal)
			{
				if (command.StartsWith(toMatch))
					return _commandMap[command];
			}

			foreach (var command in _commandsFullMatch)
			{
				if (command == toMatch)
					return _commandMap[command];
			}

			// No match found
            return Command.CMD_NOTFOUND;
        }

		/// <summary>
		/// Parses arguments
		/// </summary>
		/// <param name="input">The input to parse arguments from</param>
		/// <returns>The parsed argument string</returns>
        private static string ParseArgument(string input)
        {
			if (input == null || input == "")
				return "";

			string stmp = string.Copy(input);
            stmp = stmp.Trim();
            int i = stmp.IndexOf(' ');
            if (i != -1) { return stmp.Substring(i).Trim(); }

            return "";

        }

		/// <summary>
		/// Parses the first argument
		/// </summary>
		/// <param name="input">The input to parse arguments from</param>
		/// <returns>The first argument in the string</returns>
		private static string ParseFirstArgument(string input)
        {
			// Returns the first argument of an argument string (expects ONLY an argument
			// string -- use ParseCommand for parsing the command
			if (input == null || input == "")
				return "";

			string stmp = string.Copy(input);
            stmp = stmp.Trim();

            string[] words = stmp.Split(' ');

            if (words.Length > 0)
                return words[0];
	        else
                return "";
        }

		/// <summary>
		/// Parses exits of a room as friendly text
		/// </summary>
		/// <param name="room">The room to parse exits from</param>
		/// <returns>A string representing the room's exits</returns>
        private static string ParseExits(Room room)
		{
			try
			{
				Guard.ThrowIfNull(room, nameof(room));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(ParseExits), ex.Message);
				return "";
			}

			string parsedexits = "";

            if (room.Exits.North != null) parsedexits += "[north] ";
            if (room.Exits.East  != null) parsedexits += "[east] " ;
            if (room.Exits.South != null) parsedexits += "[south] ";
            if (room.Exits.West  != null) parsedexits += "[west] " ;
            if (room.Exits.Up    != null) parsedexits += "[up] "   ;
            if (room.Exits.Down  != null) parsedexits += "[down]"  ;

			if (parsedexits == "")
				parsedexits = "[none]";

            return parsedexits;
        }
        #endregion 
    }
}