using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;
using Hedron.Data;

namespace Hedron.Commands
{
	public static partial class CommandHandler
	{
		/// <summary>
		/// Sends a blank line
		/// </summary>
		private static CommandResult BlankLine(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Prompt), ex.Message);
				return CommandResult.CMD_R_FAIL;
			}

			// Player-only command to get/set prompt.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }

			// Do nothing -- the player's prompt will automatically be returned

			return CommandResult.CMD_R_SUCCESS;
		}

		/// <summary>
		/// Modifies the player's prompt
		/// </summary>
		private static CommandResult Prompt(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Prompt), ex.Message);
				return CommandResult.CMD_R_FAIL;
			}

			// Player-only command to get/set prompt.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }

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

			entity.IOHandler.QueueOutput("Command not yet implemented.");
			return CommandResult.CMD_R_SUCCESS;
		}

		/// <summary>
		/// Outputs player stats
		/// </summary>
		private static CommandResult Stats(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Prompt), ex.Message);
				return CommandResult.CMD_R_FAIL;
			}

			// Player-only command to show stats.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }

			var baseAspects = entity.BaseAspects;
			var baseAttributes = entity.BaseAttributes;
			var baseQualities = entity.BaseQualities;

			var modAspects = entity.GetModifiedAspects();
			var modAttributes = entity.GetModifiedAttributes();
			var modQualities = entity.GetModifiedQualities();

			var aspectsTable = TextFormatter.ToTable(2,
				// HP row
				TextFormatter.NewRow(
					3,
					"Hit Points:  ",
					$"{baseAspects.CurrentHitPoints}",
					"/",
					$"{baseAspects.MaxHitPoints}",
					""
				),
				// Stamina row
				TextFormatter.NewRow(
					3,
					"Stamina:  ",
					$"{baseAspects.CurrentStamina}",
					"/",
					$"{baseAspects.MaxStamina}",
					""
				),
				// Energy row
				TextFormatter.NewRow(
					3,
					"Energy:  ",
					$"{baseAspects.CurrentEnergy}",
					"/",
					$"{baseAspects.MaxEnergy}",
					""
				)
			);

			var attributesTable = TextFormatter.ToTable(2,
				// Essence row
				TextFormatter.NewRow(
					3,
					"Essence:  ",
					$"{baseAttributes.Essence}",
					$"[{modAttributes.Essence}]"
				),
				// Finesse row
				TextFormatter.NewRow(
					3,
					"Finesse:  ",
					$"{baseAttributes.Finesse}",
					$"[{modAttributes.Finesse}]"
				),
				// Intellect row
				TextFormatter.NewRow(
					3,
					"Intellect:  ",
					$"{baseAttributes.Intellect}",
					$"[{modAttributes.Intellect}]"
				),
				// Might row
				TextFormatter.NewRow(
					3,
					"Might:  ",
					$"{baseAttributes.Might}",
					$"[{modAttributes.Might}]"
				),
				// Spirit row
				TextFormatter.NewRow(
					3,
					"Spirit:  ",
					$"{baseAttributes.Spirit}",
					$"[{modAttributes.Spirit}]"
				),
				// Will row
				TextFormatter.NewRow(
					3,
					"Will:  ",
					$"{baseAttributes.Will}",
					$"[{modAttributes.Will}]"
				)
			);

			var qualitiesTable = TextFormatter.ToTable(2,
				// Attack and Armor row
				TextFormatter.NewRow(
					3,
					"Attack Rating:  ",
					$"{baseQualities.AttackRating}",
					$"[{modQualities.AttackRating}]",
					"Armor Rating:  ",
					$"{baseQualities.ArmorRating}",
					$"[{modQualities.ArmorRating}]"
				),
				// Critical Hit and Damage row
				TextFormatter.NewRow(
					3,
					"Critical Hit:  ",
					$"{baseQualities.CriticalHit}",
					$"[{modQualities.CriticalHit}]",
					"Critical Damage:  ",
					$"{baseQualities.CriticalDamage}",
					$"[{modQualities.CriticalDamage}]"
				)
			);

			entity.IOHandler.QueueOutput("Statistics:");
			entity.IOHandler.QueueOutput(aspectsTable + "\n");
			entity.IOHandler.QueueOutput(attributesTable + "\n");
			entity.IOHandler.QueueOutput(qualitiesTable + "\n");

			return CommandResult.CMD_R_SUCCESS;
		}
	}
}