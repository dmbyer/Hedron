using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace Bot.Services
{
    public class LoggingService
    {
        private readonly DiscordSocketClient _discord;
        private readonly CommandService _commandService;

        public LoggingService(
            DiscordSocketClient discord,
            CommandService commandService)
        {
            _discord = discord;
            _commandService = commandService;

            _discord.Log += OnLogAsync;
            _commandService.Log += OnLogAsync;
        }

        private async Task OnLogAsync(LogMessage message)
        {
            var logText = $"[{DateTime.UtcNow:MM/dd/yyyy hh:mm:ss}] {message}";

            await Console.Out.WriteLineAsync(logText);
        }
    }
}
