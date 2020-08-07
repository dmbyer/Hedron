using Discord.Commands;
using System.Threading.Tasks;

namespace Bot.Modules
{
    public class UtilityCommands : ModuleBase
    {
        [Command("ping", RunMode = RunMode.Async)]
        [Summary("Basic ping command that returns a basic message on execution.")]
        public Task PingAsync()
        {
            return ReplyAsync("Pong!");
        }
    }
}
