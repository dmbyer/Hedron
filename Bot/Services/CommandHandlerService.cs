using Bot.Models;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace Bot.Services
{
    // This service handles all input from all sources and determines if a command should be executed,
    // and executes if needed.
    public class CommandHandlerService
    {
        private readonly DiscordSocketClient _discord;
        private readonly CommandService _commandService;
        private readonly BotConfig _botConfig;
        private readonly IServiceProvider _serviceProvider;

        public CommandHandlerService(
            DiscordSocketClient discord,
            CommandService commandService,
            IOptions<BotConfig> botConfig,
            IServiceProvider serviceProvider
        )
        {
            _discord = discord;
            _commandService = commandService;
            _botConfig = botConfig.Value;
            _serviceProvider = serviceProvider;

            _discord.MessageReceived += OnMessageReceivedAsync;
        }

        private async Task OnMessageReceivedAsync(SocketMessage s)
        {
            var msg = s as SocketUserMessage;

            if (msg == null ||
               msg.Author.IsBot ||
               msg.Author.IsWebhook)
            {
                return;
            }

            var context = new SocketCommandContext(_discord, msg);
            var argPosition = 0;
            if (msg.HasStringPrefix(_botConfig.Prefix, ref argPosition) ||
                    msg.HasMentionPrefix(_discord.CurrentUser, ref argPosition))
            {
                var result = await _commandService.ExecuteAsync(context, argPosition, _serviceProvider);

                if (!result.IsSuccess)
                {
                    await context.Channel.SendMessageAsync($"Sorry, {context.User.Username} something went wrong -> {result}");
                }
                else
                {
                    await context.Message.DeleteAsync();
                }
            }
        }
    }
}
