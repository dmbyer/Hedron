using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Account.Commands
{
    /// <summary>
    /// Admin command. Displays account and character info for a named character,
    /// allowing operators to correlate a character to an account.
    /// </summary>
    public sealed class WhoisCommand : ICommand
    {
        private readonly EntityService _entityService;

        public string Name => "whois";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Show account info for a named character.";
        public string LongDescription =>
            "Displays the character entity id, account entity id, account username, " +
            "and last-login timestamp for the specified character name.";
        public string Usage => "whois <characterName>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("characterName", typeof(string), CommandArgumentKind.Token,
                Required: true, "Name of the character to look up."),
        });

        public WhoisCommand(EntityService entityService)
        {
            _entityService = entityService;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var name = context.Args.Get<string>("characterName");

            foreach (var (charId, character) in _entityService.GetAllComponents<CharacterComponent>())
            {
                if (!string.Equals(character.CharacterName, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var username = "unknown";
                if (_entityService.TryGet<AccountComponent>(character.AccountEntityId, out var account))
                    username = account.Username;

                await context.Output.WriteAsync(new PlainMessage(
                    $"Character: {character.CharacterName} (entity #{charId})\n" +
                    $"Account:   {username} (entity #{character.AccountEntityId})\n" +
                    $"Last login: {character.LastLoginUtc:u}",
                    OutputSeverity.System, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PlainMessage(
                $"No character named '{name}' found.", OutputSeverity.Error, OutputCategory.System))
                .ConfigureAwait(false);
        }
    }
}
