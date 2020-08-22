using Hedron.Commands.Item;
using Hedron.Core.Entity.Base;
using Hedron.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Commands
{
    public static class CommandHandler
    {
        /// <summary>
        /// Maps command strings to the corresponding Command
        /// </summary>
        private static Dictionary<string, Command> _commandMap = new Dictionary<string, Command>()
        {
			// Operational commands
			{"config",    new Operational.Config() },
            {"help",      new Operational.Help() },
            {"quit",      new Operational.Quit() },
            {"save",      new Operational.Save() },

			// Movement commands
			{"north",     new Movement.North() },
            {"east",      new Movement.East() },
            {"south",     new Movement.South() },
            {"west",      new Movement.West() },
            {"up",        new Movement.Up() },
            {"down",      new Movement.Down() },
            {"look",      new Movement.Look() },
            {"goto",      new Movement.Goto() },

			// Item commands
			{"drink",     new Item.Drink() },
            {"drop",      new Item.Drop() },
            {"equipment", new Item.Equipment() },
            {"get",       new Item.Get() },
            {"inventory", new Item.Inventory() },
            {"remove",    new Item.Remove() },
            {"wear",      new Item.Wear() },

			// General commands
			{"prompt",    new General.Prompt() },
            {"stats",     new General.Stats() },
            {"who",       new General.Who() },
            {"title",     new General.Title() },

			// Building commands
			{"autodig",   new Building.Autodig() },
            {"elist",     new Building.Elist() },
            {"set",       new Building.Set() },
            {"mgenerate", new Building.Mgenerate() },
            {"mlist",     new Building.Mlist() },

			// Combat commands
			{"flee",      new Combat.Flee() },
            {"kill",      new Combat.Kill() },

			// Creation commands
			{"craft",     new Creation.Craft() },

            // Shopping commands
            {"buy",       new Shopping.Buy() },
            {"sell",      new Shopping.Sell() },
            {"shop",      new Shopping.Shop() },

			// Skill commands
			{"learn",     new Skill.Learn() },
			{"skills",    new Skill.Skills() },

			// System commands
			{"shutdown",  new System.Shutdown() }
        };

        /// <summary>
        /// Retrieves available commands
        /// </summary>
        /// <param name="privilegeLevel">The maximum privilege level</param>
        /// <returns>All commands up to and including the privilege level</returns>
        public static List<string> AvailableCommands(PrivilegeLevel privilegeLevel)
        {
            var commands = _commandMap
                .Where(kvp => kvp.Value.PrivilegeLevel <= privilegeLevel)
                .Select(kvp => kvp.Value.FriendlyName)
                .ToList();

            commands.Sort();

            return commands;
        }

        /// <summary>
        /// Initializes the command list to be sorted based on the populated commands
        /// </summary>
        public static void Initialize()
        {
            _commandMap.OrderBy(kvp => kvp.Value.HasPriority)
                .ThenBy(kvp => kvp.Value.PrivilegeLevel)
                .ThenBy(kvp => kvp.Value.FriendlyName);
        }

        /// <summary>
        /// Process a command from network input
        /// </summary>
        /// <param name="input">The string input to parse</param>
        /// <param name="entity">The entity invoking the command</param>
        /// <returns>The result of the command</returns>
        public static CommandResult ProcessCommand(string input, EntityAnimate entity, List<GameState> validStates)
        {
            if (input == "\n")
                return CommandResult.Success("");

            var commandInput = ParseCommand(input);
            var argument = ParseArgument(input);
            var command = _commandMap.Where(kvp => kvp.Key.StartsWith(commandInput, StringComparison.InvariantCultureIgnoreCase) && !kvp.Value.RequiresFullMatch
                || kvp.Key.Equals(commandInput, StringComparison.InvariantCultureIgnoreCase) && kvp.Value.RequiresFullMatch)
                .FirstOrDefault()
                .Value;

            if (command == null)
                return CommandResult.NotFound(commandInput);

            // TODO: Move command failure messages based on state into properties and change the failure message here to use it from the command itself
            if (!command.ValidStates.Intersect(validStates).Any())
            {
                return CommandResult.Failure("State commands not found.");
            }

            var args = new CommandEventArgs(argument, entity, null);

            return command.Execute(args);
        }

        /// <summary>
        /// Parses a command from an input string
        /// </summary>
        /// <param name="input">The network input to parse</param>
        /// <returns>The parsed command string</returns>
        private static string ParseCommand(string input)
        {
            if (input == null || input == "")
            {
                return "";
            }

            string toMatch = input;
            toMatch = toMatch.Trim();
            int i = toMatch.IndexOf(' ');
            if (i != -1)
                toMatch = toMatch.Substring(0, toMatch.IndexOf(' '));
            toMatch = toMatch.ToLower();

            return toMatch;
        }

        /// <summary>
        /// Parses arguments
        /// </summary>
        /// <param name="input">The input to parse arguments from</param>
        /// <returns>The parsed argument string</returns>
        public static string ParseArgument(string input)
        {
            if (input == null || input == "")
                return "";

            string stmp = input;
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
        public static string ParseFirstArgument(string input)
        {
            // Returns the first argument of an argument string (expects ONLY an argument
            // string -- use ParseCommand for parsing the command
            if (input == null || input == "")
                return "";

            string stmp = input;
            stmp = stmp.Trim();

            string[] words = stmp.Split(' ');

            if (words.Length > 0)
                return words[0];
            else
                return "";
        }
    }
}