using Hedron.Core.Entities.Living;
using Hedron.Core.Entities.Properties;
using Hedron.Data;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;
using System.Collections.Generic;

namespace Hedron.Core.Commands.General
{
    public class Who : Command
    {
        /// <summary>
        /// Who command lists out currently online players.
        /// </summary>
		public Who()
        {
            FriendlyName = "who";
            ValidStates.Add(EntityState.Active);
            ValidStates.Add(EntityState.Combat);
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

            var playerList = DataAccess.GetAll<Player>(CacheType.Instance);

            var output = new OutputBuilder(
               "-=-=-=-=-=-=-=-= Who's Online? =-=-=-=-=-=-=-=-\n" +
               $"{GetPlayerTable(playerList)}\n" +
               $"-=-=-=-=-=-=-=-=-= [{ playerList.Count } Online] =-=-=-=-=-=-=-=-=-\n\n");

            return CommandResult.Success(output.Output);
        }

        private string GetPlayerTable(IEnumerable<Player> playerList)
        {
            var whoRows = new List<string>();

            foreach (var player in playerList)
            {
                whoRows.AddRange(Formatter.NewRow(
                    $"[{player.Tier.Level}]\t",
                    $"{player.Name}{GetTabsByNameLength(player.Name)}",
                    $"{player.ShortDescription}\n"));
            }

            return Formatter.ToTable(2, 2, whoRows);
        }

        /// <summary>
        /// Returns tabs for the Who List based off length of the name of the player.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private string GetTabsByNameLength(string name)
        {
            if (name.Length > 5)
            {
                return "\t";
            }

            return "\t\t";
        }
    }
}