using Hedron.Core.Entities.Properties;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;

namespace Hedron.Core.Commands.General
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

			var entity = commandEventArgs.Entity;

			var basePools = entity.BaseMaxPools;
			var baseAttributes = entity.BaseAttributes;
			var baseQualities = entity.BaseQualities;

			var modPools = entity.ModifiedPools;
			var modAttributes = entity.ModifiedAttributes;
			var modQualities = entity.ModifiedQualities;

			var poolsTable = Formatter.ToTable(2, Formatter.DefaultIndent,
				// HP row
				Formatter.NewRow(
					"Hit Points:  ",
					$"{entity.CurrentHitPoints}",
					"/",
					$"{basePools.HitPoints}",
					""
				),
				// Stamina row
				Formatter.NewRow(
					"Stamina:  ",
					$"{entity.CurrentStamina}",
					"/",
					$"{basePools.Stamina}",
					""
				),
				// Energy row
				Formatter.NewRow(
					"Energy:  ",
					$"{entity.CurrentEnergy}",
					"/",
					$"{basePools.Energy}",
					""
				)
			);

			var attributesTable = Formatter.ToTable(2, Formatter.DefaultIndent,
				// Essence row
				Formatter.NewRow(
					"Essence:  ",
					$"{baseAttributes.Essence}",
					$"[{modAttributes.Essence}]"
				),
				// Finesse row
				Formatter.NewRow(
					"Finesse:  ",
					$"{baseAttributes.Finesse}",
					$"[{modAttributes.Finesse}]"
				),
				// Intellect row
				Formatter.NewRow(
					"Intellect:  ",
					$"{baseAttributes.Intellect}",
					$"[{modAttributes.Intellect}]"
				),
				// Might row
				Formatter.NewRow(
					"Might:  ",
					$"{baseAttributes.Might}",
					$"[{modAttributes.Might}]"
				),
				// Spirit row
				Formatter.NewRow(
					"Spirit:  ",
					$"{baseAttributes.Spirit}",
					$"[{modAttributes.Spirit}]"
				),
				// Will row
				Formatter.NewRow(
					"Will:  ",
					$"{baseAttributes.Will}",
					$"[{modAttributes.Will}]"
				)
			);

			var qualitiesTable = Formatter.ToTable(2, Formatter.DefaultIndent,
				// Attack and Armor row
				Formatter.NewRow(
					"Attack Rating:  ",
					$"{baseQualities.AttackRating}",
					$"[{modQualities.AttackRating}]",
					"Armor Rating:  ",
					$"{baseQualities.ArmorRating}",
					$"[{modQualities.ArmorRating}]"
				),
				// Critical Hit and Damage row
				Formatter.NewRow(
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

			if (entity.Currency.HasAnyValue())
				output.Append("[Currency] " + entity.Currency.ToString() + "\n\n");
			else
				output.Append("[Currency] none\n\n");

			return CommandResult.Success(output.Output);
		}
	}
}