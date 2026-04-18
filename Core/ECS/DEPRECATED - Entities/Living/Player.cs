using Core.ECS.Properties;
using Hedron.Data;
using Hedron.Core.System;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using System.Net.Sockets;
using Core.ECS.Entities.Base;

namespace Core.ECS.Entities.Living
{
    public sealed class Player : EntityAnimate
	{
		/// <summary>
		/// Player's command prompt
		/// </summary>
		public string Prompt 
		{ 
			get 
			{
				if (Instance.HasValue)
				{
					EnsurePlayerComponentsInitialized();
					var playerComponent = EcsManager.World.GetComponent<PlayerConfigurationComponent>(Instance.Value);
					if (playerComponent != null)
						return playerComponent.Prompt;
				}
				return _prompt;
			}
			set 
			{
				_prompt = value;
				if (Instance.HasValue)
				{
					EnsurePlayerComponentsInitialized();
					var playerComponent = EcsManager.World.GetComponent<PlayerConfigurationComponent>(Instance.Value);
					if (playerComponent != null)
						playerComponent.Prompt = value;
				}
			}
		}

		private string _prompt = Constants.Prompt.DEFAULT_COLOR;

		/// <summary>
		/// Player configuration
		/// </summary>
		public PlayerConfiguration Configuration 
		{ 
			get 
			{
				if (Instance.HasValue)
				{
					EnsurePlayerComponentsInitialized();
					var playerComponent = EcsManager.World.GetComponent<PlayerConfigurationComponent>(Instance.Value);
					if (playerComponent != null)
						return playerComponent.Configuration;
				}
				return _configuration;
			}
			set 
			{
				_configuration = value;
				if (Instance.HasValue)
				{
					EnsurePlayerComponentsInitialized();
					var playerComponent = EcsManager.World.GetComponent<PlayerConfigurationComponent>(Instance.Value);
					if (playerComponent != null)
						playerComponent.Configuration = value;
				}
			}
		}

		private PlayerConfiguration _configuration = new PlayerConfiguration();

        // Constructor
        public Player() : base()
        {
			IOHandler = new IOHandler();
			PrivilegeLevel = Commands.PrivilegeLevel.Administrator;
			Currency += 50;
        }

		/// <summary>
		/// Ensures player-specific components are initialized if Instance is available
		/// </summary>
		private void EnsurePlayerComponentsInitialized()
		{
			if (Instance.HasValue && !EcsManager.World.HasComponent<PlayerConfigurationComponent>(Instance.Value))
			{
				var world = EcsManager.World;
				
				world.AddComponent(Instance.Value, new PlayerConfigurationComponent
				{
					Prompt = _prompt,
					Configuration = _configuration
				});
			}
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

			IOHandler?.QueueRawOutput("Goodbye!");
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
		public override T SpawnAsObject<T>(bool withEntities, uint parent)
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the player from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <param name="parent">The parent room instance ID</param>
		/// <returns>The instance ID of the spawned player. Will return null if the method is called from an instanced object.</returns>
		public override uint? Spawn(bool withEntities, uint parent)
		{
			Guard.ThrowNotImplemented(nameof(Spawn));

			return null;
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
			IOHandler?.QueueRawOutput("You have been restored to full health!");
		}
	}
}