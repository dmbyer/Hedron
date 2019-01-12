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
				return CommandResult.NullEntity();
			}

			// Player-only command to get/set prompt.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }

			// Do nothing -- the player's prompt will automatically be returned

			return CommandResult.Success("");
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
				return CommandResult.NullEntity();
			}

			// Player-only command to get/set prompt.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }

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
				return CommandResult.NullEntity();
			}

			// Player-only command to show stats.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }

			var baseAspects = entity.BaseMaxPools;
			var baseAttributes = entity.BaseAttributes;
			var baseQualities = entity.BaseQualities;

			var modAspects = entity.ModifiedPools;
			var modAttributes = entity.ModifiedAttributes;
			var modQualities = entity.ModifiedQualities;

			var poolsTable = TextFormatter.ToTable(2, TextFormatter.DefaultIndent,
				// HP row
				TextFormatter.NewRow(
					"Hit Points:  ",
					$"{entity.CurrentHitPoints}",
					"/",
					$"{baseAspects.HitPoints}",
					""
				),
				// Stamina row
				TextFormatter.NewRow(
					"Stamina:  ",
					$"{entity.CurrentStamina}",
					"/",
					$"{baseAspects.Stamina}",
					""
				),
				// Energy row
				TextFormatter.NewRow(
					"Energy:  ",
					$"{entity.CurrentEnergy}",
					"/",
					$"{baseAspects.Energy}",
					""
				)
			);

			var attributesTable = TextFormatter.ToTable(2, TextFormatter.DefaultIndent,
				// Essence row
				TextFormatter.NewRow(
					"Essence:  ",
					$"{baseAttributes.Essence}",
					$"[{modAttributes.Essence}]"
				),
				// Finesse row
				TextFormatter.NewRow(
					"Finesse:  ",
					$"{baseAttributes.Finesse}",
					$"[{modAttributes.Finesse}]"
				),
				// Intellect row
				TextFormatter.NewRow(
					"Intellect:  ",
					$"{baseAttributes.Intellect}",
					$"[{modAttributes.Intellect}]"
				),
				// Might row
				TextFormatter.NewRow(
					"Might:  ",
					$"{baseAttributes.Might}",
					$"[{modAttributes.Might}]"
				),
				// Spirit row
				TextFormatter.NewRow(
					"Spirit:  ",
					$"{baseAttributes.Spirit}",
					$"[{modAttributes.Spirit}]"
				),
				// Will row
				TextFormatter.NewRow(
					"Will:  ",
					$"{baseAttributes.Will}",
					$"[{modAttributes.Will}]"
				)
			);

			var qualitiesTable = TextFormatter.ToTable(2, TextFormatter.DefaultIndent,
				// Attack and Armor row
				TextFormatter.NewRow(
					"Attack Rating:  ",
					$"{baseQualities.AttackRating}",
					$"[{modQualities.AttackRating}]",
					"Armor Rating:  ",
					$"{baseQualities.ArmorRating}",
					$"[{modQualities.ArmorRating}]"
				),
				// Critical Hit and Damage row
				TextFormatter.NewRow(
					"Critical Hit:  ",
					$"{baseQualities.CriticalHit}",
					$"[{modQualities.CriticalHit}]",
					"Critical Damage:  ",
					$"{baseQualities.CriticalDamage}",
					$"[{modQualities.CriticalDamage}]"
				)
			);

			var output = new OutputBuilder(
				"Statistics:\n" +
				poolsTable + "\n\n" +
				attributesTable + "\n\n" +
				qualitiesTable + "\n\n"
				);

			return CommandResult.Success(output.Output);
		}
	}
}