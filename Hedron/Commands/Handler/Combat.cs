using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Combat;
using Hedron.Core;
using Hedron.Data;
using Hedron.System;

namespace Hedron.Commands
{
	public static partial class CommandHandler
	{
		/// <summary>
		/// Runs from combat
		/// </summary>
		private static CommandResult Flee(string argument, EntityAnimate entity)
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
			
			if (entity.StateHandler.State != Network.StateHandler.GameState.Combat)
			{
				return CommandResult.Failure("You have nothing to flee from.");
			}

			CombatHandler.Exit(entity.Instance);
			
			return CommandResult.Success("You run from combat.");
		}

		/// <summary>
		/// Initiates combat
		/// </summary>
		private static CommandResult Kill(string argument, EntityAnimate entity)
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

			if (entity.StateHandler.State == Network.StateHandler.GameState.Combat)
			{
				return CommandResult.Failure("You are already in combat!");
			}
			else if (entity.StateHandler.State != Network.StateHandler.GameState.Active)
			{
				Logger.Error(nameof(CommandHandler), nameof(Kill), $"Unexpected entity state: {entity.StateHandler.State}.");
				return CommandResult.Failure($"Unexpected entity state: {entity.StateHandler.State}.");
			}

			var room = EntityContainer.GetInstanceParent<Room>(entity.Instance);
			var entities = DataAccess.GetMany<EntityAnimate>(room.GetAllEntities<EntityAnimate>(), CacheType.Instance);
			uint? targetID = null;

			// Find first matching target
			foreach (var ent in entities)
			{
				if (ent.Instance != targetID && ent.Name.StartsWith(argument))
				{
					targetID = ent.Instance;
					break;
				}
			}

			if (targetID != null)
			{
				CombatHandler.Enter(entity.Instance, targetID, false);
			}
			else
			{
				return CommandResult.Failure("There is no such target.");
			}

			var targetName = DataAccess.Get<EntityAnimate>(targetID, CacheType.Instance).ShortDescription;
			
			return CommandResult.Success($"You attack {targetName}!");
		}
	}
}