using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using Hedron.Core;
using Hedron.System;
using Hedron.Data;
using Hedron.Network;
using Hedron.Commands;

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
			bool shutdown = false;
			do
			{
				// Accept pending clients
				if (server.Pending())
				{
					TcpClient client = server.AcceptTcpClient();
					clients.Add(client);

					Player player = new Player(client.GetStream());
					player.Instance = DataAccess.Add<Player>(player, CacheType.Instance);

					// Player connected -- get name:
					player.IOHandler.QueueOutput("Welcome! Please enter your player name: ");
					player.IOHandler.SendOutput();
				}

				// Process input/output on all existing players
				foreach (var player in DataAccess.GetAll<Player>(CacheType.Instance))
				{
					var inputResult = player.IOHandler.RetrieveInput();
					var commandResult = CommandResult.CMD_R_FAIL;

					if (inputResult.StatusCode == Constants.IO_READ.PENDINGREAD)
						continue;

					// Process the input based on state
					switch (inputResult.StatusCode)
					{
						case Constants.IO_READ.SUCCESSREAD:
							// Process player input on successful read
							commandResult = player.StateHandler.ProcessInput(inputResult.Data, player);
							break;
						case Constants.IO_READ.SENDEXCEED:
							// Disconnect player if they have exceeded their sent data
							PendingPlayerConnectionClosures.Add(player);
							continue;
					}

					// Handle command result and push appropriate output
					switch (commandResult)
					{
						case CommandResult.CMD_R_FAIL:
							Logger.Error(nameof(Main), "switch (commandResult)", player.Name + ": CMD_R_FAIL: " + inputResult.Data);
							break;
						case CommandResult.CMD_R_SUCCESS:
							break;
						case CommandResult.CMD_R_ERRSYNTAX:
							break;
						case CommandResult.CMD_R_INVALID_ENTITY:
							break;
						case CommandResult.CMD_R_NOTFOUND:
							player.IOHandler.QueueOutput("Invalid command.");
							break;
						case CommandResult.CMD_R_QUIT:
							// Disconnect player if they have quit
							Logger.Info(nameof(Main), "switch (commandResult)", player.Name + " quit.");
							PendingPlayerConnectionClosures.Add(player);
							break;
						case CommandResult.CMD_R_SHUTDOWN:
							// Shut down the server if requested
							Logger.Info(nameof(Main), "switch (commandResult)", player.Name + " requested shutdown.");
							shutdown = true;
							break;
					}

					player.IOHandler.QueueOutput("\n" + player.GetParsedPrompt());

					// Send player output
					player.IOHandler.SendOutput();
				}

				// TODO: "Tick" to update game world (only tick once every WORLD_TICK_TIME
				// TODO: Implement synchronous event handling
				// Send player output again
				Combat.CombatHandler.ProcessAllEntityCombatRound();

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

			} while (!shutdown);

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
