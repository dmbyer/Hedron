using Hedron.Core.Entity.Base;
using Hedron.Data;
using Hedron.Network;
using Hedron.System;
using System.Net.Sockets;

namespace Hedron.Core.Entity.Living
{
    public sealed class Player : EntityAnimate
	{
		// Public Fields
		public string Prompt { get; set; } = Constants.Prompt.DEFAULT_COLOR;
		public PlayerConfiguration Configuration { get; set; } = new PlayerConfiguration();

        // Constructor
        public Player(NetworkStream stream) : base()
        {
            IOHandler = new IOHandler(this, stream);

			StateHandler.State = GameState.NameSelection;
			PrivilegeLevel = Commands.PrivilegeLevel.Administrator;
        }
		
        public void Save()
        {
            
        }

        public void Load()
        {
           
        }

		/// <summary>
		/// Cleanly exits the player from the game.
		/// </summary>
		public void Exit()
		{
			// TODO: Remove from combat etc.

			IOHandler.QueueOutput("Goodbye!");
		}

		/// <summary>
		/// Parses the player's prompt based on variables into text
		/// </summary>
		/// <returns>The parsed prompt</returns>
        public string GetParsedPrompt()
        {
            string parsed = Prompt;

            parsed = parsed.Replace(Constants.Prompt.HP_CURRENT, CurrentHitPoints.ToString());
            parsed = parsed.Replace(Constants.Prompt.HP_MAX, ModifiedPools.HitPoints.ToString());

            parsed = parsed.Replace(Constants.Prompt.STAMINA_CURRENT, CurrentStamina.ToString());
            parsed = parsed.Replace(Constants.Prompt.STAMINA_MAX, ModifiedPools.Stamina.ToString());

            parsed = parsed.Replace(Constants.Prompt.ENERGY_CURRENT, CurrentEnergy.ToString());
            parsed = parsed.Replace(Constants.Prompt.ENERGY_MAX, ModifiedPools.Energy.ToString());

            return parsed;
		}

		/// <summary>
		/// Spawns an instance of the player from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <param name="parent">The parent room instance ID</param>
		/// <returns>The spawned player. Will return null if the method is called from an instanced object.</returns>
		public override T SpawnAsObject<T>(bool withEntities, uint? parent = null)
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the player from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <param name="parent">The parent room instance ID</param>
		/// <returns>The instance ID of the spawned player. Will return null if the method is called from an instanced object.</returns>
		public override uint? Spawn(bool withEntities, uint? parent = null)
		{
			Guard.ThrowNotImplemented(nameof(Spawn));

			return null;
		}


		override protected void OnCacheObjectRemoved(object source, CacheObjectEventArgs args)
		{

		}

		override protected void OnObjectDestroyed(object source, CacheObjectEventArgs args)
		{

		}

		/// <summary>
		/// Drops a corpse with all items and restores player to full health.
		/// </summary>
		protected override void HandleDeath(object source, CacheObjectEventArgs args)
		{
			base.HandleDeath(source, args);
			ModifyCurrentHealth((int)ModifiedPools.HitPoints, false);
			IOHandler.QueueOutput("You have been restored to full health!");
		}
	}
}