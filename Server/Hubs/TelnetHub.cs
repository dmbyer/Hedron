using Hedron.Core.Commands;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Living;
using Hedron.Core.System;
using Hedron.Data;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Network
{
    /// <summary>
    /// Singleton Telnet Hub
    /// </summary>
    public sealed class TelnetHub
    {
        private static TelnetHub _instance;

        private readonly Dictionary<string, TelnetClient> _telnetClients;
		private readonly TcpListener _telnetServer;
        private readonly StateHandler _stateHandler;

        /// <summary>
        /// Private constructor for singleton pattern
        /// </summary>
        private TelnetHub()
        {
            _telnetClients = new Dictionary<string, TelnetClient>();
            _telnetServer = new TcpListener(IPAddress.Parse(TelnetConfig.SERVER_LISTEN_IP), TelnetConfig.SERVER_LISTEN_PORT);
            _stateHandler = new StateHandler();
        }

        /// <summary>
        /// Processes accepting/closing telnet connections and telnet input/outpit
        /// </summary>
        public void ProcessConnections()
        {
            var clientsToRemove = new List<string>();
            Console.WriteLine("Started processing telnet connections.");
            _telnetServer.Start();
            do
            {
                // Accept connections
                if (_telnetServer.Pending())
                {
                    var connectionID = Guid.NewGuid().ToString();
                    TelnetClient client = new TelnetClient(_telnetServer.AcceptTcpClient(), connectionID);
                    _telnetClients.Add(connectionID, client);

					var player = new Player
					{
						ConnectionID = connectionID
					};

                    DataAccess.Add<Player>(player, CacheType.Instance, null, false);

					player.IOHandler.QueueRawOutput("Welcome! Please enter your player name: ");
                }

                // Process input
                foreach (var client in _telnetClients)
                {
                    var read = client.Value.RetrieveInput();

                    if (read.StatusCode == TelnetConfig.IO_READ.PENDINGREAD)
                        continue;

                    var entity = DataAccess.GetAll<EntityAnimate>(CacheType.Instance).Find(e => e.ConnectionID == client.Key);

                    // Process the input based on state
                    switch (read.StatusCode)
                    {
                        case TelnetConfig.IO_READ.SUCCESSREAD:
                            // Process player input on successful read
                            if (entity == null)
							{
                                Logger.Info(nameof(TelnetHub), nameof(ProcessConnections), $"Data read from client {client.Key} with no associated entity. Closing connection.");
                                clientsToRemove.Add(client.Key);
                                continue;
							}

                            var delimitedInput = read.Data.Split(";");
                            foreach (var line in delimitedInput)
                                entity.IOHandler.QueueRawInput(line);

                            var result = _stateHandler.ProcessInput(entity.IOHandler.DequeueRawInput(), entity);

                            // Handle command result and push appropriate output
                            switch (result.ResultCode)
                            {
                                case ResultCode.FAIL:
                                    Logger.Info(nameof(TelnetHub), nameof(ProcessConnections), result.ResultMessage);
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
                                    clientsToRemove.Add(client.Key);
                                    break;
                            }

                            // Send command result to player
                            entity.IOHandler?.QueueRawOutput(result.ResultMessage);
                            break;
                        case TelnetConfig.IO_READ.SENDEXCEED:
                            // Disconnect player if they have exceeded their sent data
                            Logger.Info(nameof(TelnetHub), nameof(ProcessConnections), $"{entity.Name} [{client.Key}]: Reached send exceed.");
                            clientsToRemove.Add(client.Key);
                            continue;
                    }

                    if (entity.GetType() == typeof(Player) && entity.State != Core.Entities.Properties.EntityState.NameSelection)
                    {
                        entity.IOHandler?.QueueRawOutput("\n" + ((Player)entity).GetParsedPrompt());
                    }
                    else
					{
                        entity.IOHandler?.QueueRawOutput("\n");
                    }
                }

                // Process output
                foreach (var client in _telnetClients)
                {
                    var entity = DataAccess.GetAll<EntityAnimate>(CacheType.Instance).Find(e => e.ConnectionID == client.Key);
                    // Send player output
                    while (entity.IOHandler.RawOutputCount > 0)
                        client.Value.QueueOutput(entity.IOHandler.DequeueRawOutput());

                    if (entity.GetType() == typeof(Player))
                    {
                        client.Value.SendOutput(((Player)entity).Configuration.UseColor);
                    }
                    else
                    {
                        client.Value.SendOutput(false);
                    }
                }

                foreach (var client in clientsToRemove)
                {
                    var entity = DataAccess.GetAll<EntityAnimate>(CacheType.Instance).Find(e => e.ConnectionID == client);
                    if (entity != null)
					{
                        entity.ConnectionID = null;
                        entity.IOHandler = null;
					}
                    _telnetClients[client].CloseConnection();
                    _telnetClients.Remove(client);
				}

                Thread.Sleep(10);
            } while (true);
        }

        /// <summary>
        /// Sends message to all Telnet clients
        /// </summary>
        /// <param name="message">The message to send to the client</param>
        public void Send(string message)
        {
            foreach (var client in _telnetClients)
            {
                client.Value.QueueOutput(message);
            }
        }

        /// <summary>
        /// Sends message to a specific Telnet client
        /// </summary>
        /// <param name="message">The message to send to the client</param>
        /// <param name="hubId">The Telnet connection ID</param>
        public void SendToClient(string message, string hubId)
        {
            try
            {
                _telnetClients.TryGetValue(hubId, out TelnetClient client);
                client.QueueOutput(message);
            }
            catch
            {
                Logger.Error(nameof(TelnetHub), nameof(SendToClient), $"Invalid hubID ({hubId}) for sending message to client.");
                return;
			}
        }

        /// <summary>
        /// Retrieves TelnetHub singleton instance
        /// </summary>
        public static TelnetHub Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new TelnetHub();

                return _instance;
            }
        }
    }
}
