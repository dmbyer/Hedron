using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Property;
using Hedron.System;
using Hedron.System.Exceptions;

namespace Hedron.Commands.General
{
	/// <summary>
	/// Outputs entity stats
	/// </summary>
	public class Stats : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Stats()
		{
			FriendlyName = "stats";
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

			var entity = commandEventArgs.Entity;

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
