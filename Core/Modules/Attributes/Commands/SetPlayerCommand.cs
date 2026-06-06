using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Attributes.Events;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Attributes.Commands
{
    public sealed class SetPlayerCommand : ICommand
    {
        private readonly IAttributeSystem _attributeSystem;
        private readonly ISessionManager _sessionManager;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;

        public string Name => "setplayer";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Set an attribute on a connected player.";
        public string LongDescription =>
            "Sets a stat on a currently-connected player by character name. " +
            "Valid properties: level, hp, mind, body, spirit, attunement, mana, maxmana, stamina, maxstamina, astra, maxastra.";
        public string Usage => "setplayer <characterName> <property> <value>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("characterName", typeof(string), CommandArgumentKind.Token,
                Required: true, "Name of the connected character."),
            new CommandArgument("property", typeof(string), CommandArgumentKind.Token,
                Required: true, "Property to set: level, hp, mind, body, spirit, attunement, mana, maxmana, stamina, maxstamina, astra, maxastra."),
            new CommandArgument("value", typeof(string), CommandArgumentKind.Token,
                Required: true, "New numeric value."),
        });

        public SetPlayerCommand(
            IAttributeSystem attributeSystem,
            ISessionManager sessionManager,
            EntityService entityService,
            IEventBus eventBus,
            IPersistenceSystem persistence)
        {
            _attributeSystem = attributeSystem;
            _sessionManager = sessionManager;
            _entityService = entityService;
            _eventBus = eventBus;
            _persistence = persistence;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var characterName = context.Args.Get<string>("characterName");
            var property = context.Args.Get<string>("property").ToLowerInvariant();
            var rawValue = context.Args.Get<string>("value");

            if (!int.TryParse(rawValue, out var value) || value < 1)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    "Value must be a positive integer.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            uint playerEntityId = 0;
            foreach (var session in _sessionManager.GetAll())
            {
                if (session.PlayerEntityId == 0)
                    continue;
                if (_entityService.TryGet<CharacterComponent>(session.PlayerEntityId, out var ch) &&
                    string.Equals(ch.CharacterName, characterName, StringComparison.OrdinalIgnoreCase))
                {
                    playerEntityId = session.PlayerEntityId;
                    break;
                }
            }

            if (playerEntityId == 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"No connected player named '{characterName}'.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            switch (property)
            {
                case "level":
                    _attributeSystem.SetLevel(playerEntityId, value);
                    break;

                case "hp":
                    _attributeSystem.SetMaxHp(playerEntityId, value);
                    break;

                case "mind":
                    _attributeSystem.SetMind(playerEntityId, value);
                    break;

                case "body":
                    _attributeSystem.SetBody(playerEntityId, value);
                    break;

                case "spirit":
                    _attributeSystem.SetSpirit(playerEntityId, value);
                    break;

                case "attunement":
                    _attributeSystem.SetAttunement(playerEntityId, value);
                    break;

                case "mana":
                    _attributeSystem.SetCurrentMana(playerEntityId, value);
                    break;

                case "maxmana":
                    _attributeSystem.SetMaxMana(playerEntityId, value);
                    break;

                case "stamina":
                    _attributeSystem.SetCurrentStamina(playerEntityId, value);
                    break;

                case "maxstamina":
                    _attributeSystem.SetMaxStamina(playerEntityId, value);
                    break;

                case "astra":
                    _attributeSystem.SetCurrentAstra(playerEntityId, value);
                    break;

                case "maxastra":
                    _attributeSystem.SetMaxAstra(playerEntityId, value);
                    break;

                default:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Unknown property '{property}'. Valid properties: level, hp, mind, body, spirit, attunement, mana, maxmana, stamina, maxstamina, astra, maxastra.",
                        OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                    return;
            }

            await _persistence.SaveEntityAsync(playerEntityId).ConfigureAwait(false);

            await _eventBus.PublishAsync(new PlayerAttributeSetByAdminEvent(
                context.InvokerEntityId,
                playerEntityId,
                property,
                rawValue)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Player {characterName} {property} set to {value}.",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }
    }
}
