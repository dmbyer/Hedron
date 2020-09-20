using Hedron.Data;
using Hedron.Core.Commands;
using Hedron.Core.Locale;
using Hedron.Core.System;
using Hedron.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Server
{
	public class Program
	{
		public static void Main(string[] args)
		{
			DataPersistence.PersistencePath = Constants.DEFAULT_WORLD_FOLDER;

			try
			{
				World.LoadWorld(false);
			}
			catch
			{
				// Throw error if the world directory exists but some error still occurred
				if (Directory.Exists(DataPersistence.PersistencePath))
					throw;

				// .. otherwise, there was no world to load, so create a new one

				// Wipe cache for safety
				DataAccess.WipeCache();

				// Create a new world and save to disk
				World.NewPrototype();
			}

			/*
			// Declare tick timers
			// ...
			var tickTimer = DateTime.UtcNow;

			// TODO: Update to use an event-driven heartbeat
			// TODO: Update area respawn to function based off heartbeat
			// TODO: Implement synchronous event handling
			// Send player output again
			if ((DateTime.UtcNow - tickTimer).TotalSeconds >= 1)
			{
				tickTimer = DateTime.UtcNow;
				Combat.CombatHandler.ProcessAllEntityCombatRound();

				var areas = DataAccess.GetAll<Area>(CacheType.Instance);

				foreach (var area in areas)
					area.Respawn();
			}
			*/

			DataAccess.GetAll<World>(CacheType.Prototype)?[0].Spawn(true);
			CommandService.Initialize();
			Task.Run(TelnetHub.Instance.ProcessConnections);
			CreateHostBuilder(args).Build().Run();

		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.UseStartup<Startup>();
				});
	}
}
