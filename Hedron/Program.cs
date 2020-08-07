using Hedron.Commands;
using Hedron.Core.Entity.Living;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Network;
using Hedron.System;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Hedron
{
    public class Program
	{
		private static Thread gameLoop;

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

			CommandHandler.Initialize();

			gameLoop = new Thread(new ThreadStart(GameLoop));
			gameLoop.Start();

			CreateWebHostBuilder(args).Build().Run();
		}

		public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
			WebHost.CreateDefaultBuilder(args)
				.UseStartup<Startup>();

		public static void GameLoop()
		{
			// Declare input read timer
			// ...

			// Declare tick timer
			// ...
			var combatTimer = DateTime.UtcNow;

			// List of TcpClients
			List<TcpClient> clients = new List<TcpClient>();

			// Players pending a connection closure
			List<Player> PendingPlayerConnectionClosures = new List<Player>();

			// Instantiate initial world
			DataAccess.GetAll<World>(CacheType.Prototype)?[0].Spawn(true);

			// Start listening for client requests.
			int port = Constants.SERVER_LISTEN_PORT;
			IPAddress localAddr = IPAddress.Parse(Constants.SERVER_LISTEN_IP);
			TcpListener server = new TcpListener(localAddr, port);
			server.Start();

			// TODO: Implement shutdown timer (use World.Instance?) -- Shutdown command should allow timer argument
			do
			{
				// Accept pending clients
				if (server.Pending())
				{
					TcpClient client = server.AcceptTcpClient();
					clients.Add(client);

					Player player = new Player(client.GetStream()) { ShortDescription = "the Adventurer." };
					player.Instance = DataAccess.Add<Player>(player, CacheType.Instance);

					// Player connected -- get name:
					player.IOHandler.QueueOutput("Welcome! Please enter your player name: ");
					player.IOHandler.SendOutput();
				}

				// Process input/output on all existing players
				foreach (var player in DataAccess.GetAll<Player>(CacheType.Instance))
				{
					var inputResult = player.IOHandler.RetrieveInput();
					CommandResult commandResult = null;

					if (inputResult.StatusCode == Constants.IO_READ.PENDINGREAD)
						continue;

					// Process the input based on state
					switch (inputResult.StatusCode)
					{
						case Constants.IO_READ.SUCCESSREAD:
							// Process player input on successful read
							commandResult = player.StateHandler.ProcessInput(inputResult.Data, player);

							// Handle command result and push appropriate output
							switch (commandResult.ResultCode)
							{
								case ResultCode.FAIL:
									Logger.Info(nameof(StateHandler), nameof(StateHandler.ProcessInput), commandResult.ResultMessage);
									break;
								case ResultCode.SUCCESS:
									break;
								case ResultCode.ERR_SYNTAX:
									break;
								case ResultCode.INVALID_ENTITY:
									break;
								case ResultCode.NOT_FOUND:
									break;
								case ResultCode.QUIT:
									// Disconnect player if they have quit
									PendingPlayerConnectionClosures.Add(player);
									break;
							}

							// Send command result to player
							player.IOHandler.QueueOutput(commandResult.ResultMessage);

							break;
						case Constants.IO_READ.SENDEXCEED:
							// Disconnect player if they have exceeded their sent data
							Logger.Info(nameof(Main), nameof(GameLoop), $"{player.Name} [{player.Prototype}]: Reached send exceed.");
							PendingPlayerConnectionClosures.Add(player);
							continue;
					}

					player.IOHandler.QueueOutput("\n" + player.GetParsedPrompt());

					// Send player output
					player.IOHandler.SendOutput();
				}

				// TODO: "Tick" to update game world (only tick once every WORLD_TICK_TIME
				// TODO: Implement synchronous event handling
				// Send player output again
				if ((DateTime.UtcNow - combatTimer).TotalSeconds >= 1)
				{
					combatTimer = DateTime.UtcNow;
					Combat.CombatHandler.ProcessAllEntityCombatRound();
				}

				// Process pending socket closures and remove players from world
				if (PendingPlayerConnectionClosures.Count > 0)
				{
					foreach (Player player in PendingPlayerConnectionClosures)
					{
						player.Exit();
						DataAccess.Remove<Player>(player.Instance, CacheType.Instance);
						player.IOHandler.SendOutput();
						player.IOHandler.CloseConnection();
					}
					PendingPlayerConnectionClosures.Clear();
				}

				// Sleep 5 ms
				Thread.Sleep(5);

			} while (!World.Shutdown);

			// Time to shutdown!
			foreach (var p in DataAccess.GetAll<Player>(CacheType.Instance))
			{
				p.IOHandler.CloseConnection();
			}

			// TODO: Do some cleanup like saving characters etc.
			server.Stop();

			return;
		}
	}
}
