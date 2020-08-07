using Bot.Models;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Bot.Services
{
    public class StartupService
    {
        private readonly IServiceProvider _provider;
        private readonly DiscordSocketClient _discord;
        private readonly CommandService _commandService;
        private readonly BotConfig _botConfig;

        public StartupService(
            IServiceProvider provider,
            DiscordSocketClient discord,
            CommandService commandService,
            IOptions<BotConfig> botConfig)
        {
            _provider = provider;
            _discord = discord;
            _commandService = commandService;
            _botConfig = botConfig.Value;
        }

        public async Task StartAsync()
        {
            var discordToken = _botConfig.Tokens.Discord;

            if (string.IsNullOrEmpty(discordToken))
            {
                throw new NullReferenceException("There was no Discord token provided in the BotConfig.json.");
            }

            await _discord.LoginAsync(TokenType.Bot, discordToken);
            await _discord.StartAsync();

            await _commandService.AddModulesAsync(Assembly.GetEntryAssembly(), _provider);
        }
    }
}
