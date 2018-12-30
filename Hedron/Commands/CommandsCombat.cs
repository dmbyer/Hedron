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
		/// Sends a blank line
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
				return CommandResult.CMD_R_FAIL;
			}

			if (entity.StateHandler.State == Network.StateHandler.GameState.Combat)
			{
				entity.IOHandler?.QueueOutput("You are already in combat!");
				return CommandResult.CMD_R_FAIL;
			}
			else if (entity.StateHandler.State != Network.StateHandler.GameState.Active)
			{
				Logger.Error(nameof(CommandHandler), nameof(Kill), $"Unexpected entity state: {entity.StateHandler.State}.");
				return CommandResult.CMD_R_FAIL;
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
				entity.IOHandler?.QueueOutput("There is no such target.");
				return CommandResult.CMD_R_INVALID_ENTITY;
			}
			
			return CommandResult.CMD_R_SUCCESS;
		}
	}
}