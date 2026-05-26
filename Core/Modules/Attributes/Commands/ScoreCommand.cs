using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Attributes.Commands
{
    public sealed class ScoreCommand : ICommand
    {
        private readonly EntityService _entityService;

        public string Name => "score";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Display your character stats.";
        public string LongDescription => "Shows your level, hit points, and base combat stats.";
        public string Usage => "score";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new(Array.Empty<CommandArgument>());

        public ScoreCommand(EntityService entityService)
        {
            _entityService = entityService;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var entityId = context.InvokerEntityId;

            var charName = _entityService.TryGet<CharacterComponent>(entityId, out var ch)
                ? ch.CharacterName
                : "Unknown";

            var attrs = _entityService.TryGet<AttributesComponent>(entityId, out var a)
                ? a
                : new AttributesComponent();

            var pools = _entityService.TryGet<PoolsComponent>(entityId, out var p)
                ? p
                : new PoolsComponent();

            await context.Output.WriteAsync(new ScoreDisplayMessage(
                charName,
                attrs.Level,
                pools.CurrentHp,
                pools.MaxHp,
                attrs.Strength,
                attrs.Dexterity,
                attrs.Constitution)).ConfigureAwait(false);
        }
    }
}
