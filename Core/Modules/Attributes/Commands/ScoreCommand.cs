using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Attributes.Commands
{
    public sealed class ScoreCommand : ICommand
    {
        private readonly EntityService _entityService;
        private readonly IStatSystem _statSystem;

        public string Name => "score";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Display your character stats.";
        public string LongDescription => "Shows your level, hit points, mana, stamina, astra, and base attributes.";
        public string Usage => "score";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new(Array.Empty<CommandArgument>());

        public ScoreCommand(EntityService entityService, IStatSystem statSystem)
        {
            _entityService = entityService;
            _statSystem = statSystem;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var entityId = context.InvokerEntityId;

            var charName = _entityService.TryGet<CharacterComponent>(entityId, out var ch)
                ? ch.CharacterName
                : "Unknown";

            var level = _entityService.TryGet<AttributesComponent>(entityId, out var a)
                ? a.Level
                : 1;

            await context.Output.WriteAsync(new ScoreDisplayMessage(
                charName,
                level,
                _statSystem.Get(entityId, ScoreId.HpCurrent),
                _statSystem.Get(entityId, ScoreId.HpMax),
                _statSystem.Get(entityId, ScoreId.Mind),
                _statSystem.Get(entityId, ScoreId.Body),
                _statSystem.Get(entityId, ScoreId.Spirit),
                _statSystem.Get(entityId, ScoreId.Attunement),
                _statSystem.Get(entityId, ScoreId.ManaCurrent),
                _statSystem.Get(entityId, ScoreId.ManaMax),
                _statSystem.Get(entityId, ScoreId.StaminaCurrent),
                _statSystem.Get(entityId, ScoreId.StaminaMax),
                _statSystem.Get(entityId, ScoreId.AstraCurrent),
                _statSystem.Get(entityId, ScoreId.AstraMax))).ConfigureAwait(false);
        }
    }
}
