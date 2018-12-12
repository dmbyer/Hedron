using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Hedron.Core;
using Hedron.System;
using Hedron.Data;
using Hedron.Network;

namespace Hedron
{
	public sealed class Player : EntityAnimate
	{
		// Public Fields
		public string Prompt { get; set; }
		public Flags.PlayerRights Rights { get; set; } = 0;
		public StateHandler StateHandler { get; set; } = new StateHandler();
		public PlayerConfiguration Configuration { get; set; } = new PlayerConfiguration();

        // Constructor
        public Player(NetworkStream stream) : base()
        {
            IOHandler = new IOHandler(this, stream);
            Prompt = string.Copy(Constants.Prompt.DEFAULT);

			StateHandler.InputState = StateHandler.State.NameSelection;
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
            string parsed = string.Copy(Prompt);

            parsed = parsed.Replace(Constants.Prompt.HP_CURRENT, BaseAspects.CurrentHitPoints.ToString());
            parsed = parsed.Replace(Constants.Prompt.HP_MAX, BaseAspects.MaxHitPoints.ToString());

            parsed = parsed.Replace(Constants.Prompt.STAMINA_CURRENT, BaseAspects.CurrentStamina.ToString());
            parsed = parsed.Replace(Constants.Prompt.STAMINA_MAX, BaseAspects.MaxStamina.ToString());

            parsed = parsed.Replace(Constants.Prompt.ENERGY_CURRENT, BaseAspects.CurrentEnergy.ToString());
            parsed = parsed.Replace(Constants.Prompt.ENERGY_MAX, BaseAspects.MaxEnergy.ToString());

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


		override protected void OnCacheObjectRemoved(object source, CacheObjectRemovedEventArgs args)
		{

		}

		override protected void OnObjectDestroyed(object source, CacheObjectRemovedEventArgs args)
		{
			
		}
	}
}