using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection.Commands
{
    /// <summary>
    /// Admin/designer command <c>power [target]</c>. Resolves a runtime-in-world target — self
    /// (default), an item in inventory/room, or a mob in the room — to a <see cref="PowerSnapshot"/>
    /// and prints the computed power scalar and its classified tier band. Blueprint-id/template
    /// resolution is deferred to the Blazor editor readout (resolved Q2 — see
    /// docs/features/progression/power-budget-system.md).
    /// </summary>
    public sealed class PowerCommand : ICommand
    {
        private static readonly ScoreId[] AllScores = (ScoreId[])Enum.GetValues(typeof(ScoreId));

        private readonly IPowerBudgetSystem _powerBudget;
        private readonly IStatSystem _statSystem;
        private readonly IItemSystem _itemSystem;
        private readonly ICombatSystem _combatSystem;
        private readonly EntityService _entityService;

        public string Name => "power";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Inspect a target's computed power and tier band.";
        public string LongDescription =>
            "Resolves a runtime-in-world target — self (default), an item in your inventory/room, " +
            "or a mob in your room — to a score snapshot, then prints its computed power scalar " +
            "and classified tier band (0-6). Blueprint/template inspection lives in the Blazor editor.";
        public string Usage => "power [target]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("target", typeof(string), CommandArgumentKind.RestOfLine,
                Required: false, "Name/keyword of the item or mob to inspect (omit for yourself)."),
        });

        public PowerCommand(
            IPowerBudgetSystem powerBudget,
            IStatSystem statSystem,
            IItemSystem itemSystem,
            ICombatSystem combatSystem,
            EntityService entityService)
        {
            _powerBudget = powerBudget;
            _statSystem = statSystem;
            _itemSystem = itemSystem;
            _combatSystem = combatSystem;
            _entityService = entityService;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            context.Args.TryGet<string>("target", out var target);
            target = target?.Trim() ?? string.Empty;

            if (target.Length == 0 || string.Equals(target, "self", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(target, "me", StringComparison.OrdinalIgnoreCase))
            {
                await WriteReadout(context, "You", LiveEntitySnapshot(context.InvokerEntityId), tier: 0, authoredBand: null)
                    .ConfigureAwait(false);
                return;
            }

            _entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location);

            if (_itemSystem.TryFindItemInInventory(context.InvokerEntityId, target, out var invItemId) ||
                (location != null && _itemSystem.TryFindItemInRoom(location.RoomEntityId, target, out invItemId)))
            {
                var item = _entityService.Get<ItemDataComponent>(invItemId);
                await WriteReadout(context, item.Name, ItemSnapshot(item), item.TierBand, item.TierBand)
                    .ConfigureAwait(false);
                return;
            }

            if (location != null && _combatSystem.TryFindTargetInRoom(location.RoomEntityId, target, out var mobId))
            {
                var mob = _entityService.Get<MobDataComponent>(mobId);
                await WriteReadout(context, mob.Name, LiveEntitySnapshot(mobId), mob.TierBand, mob.TierBand)
                    .ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PlainMessage(
                "You don't see that here.", OutputSeverity.System, OutputCategory.System)).ConfigureAwait(false);
        }

        private async Task WriteReadout(CommandContext context, string label, PowerSnapshot snapshot, int tier, int? authoredBand)
        {
            var power = _powerBudget.Estimate(snapshot, tier);
            var band = _powerBudget.Classify(power);
            await context.Output.WriteAsync(new PowerReadoutMessage(label, power, band, authoredBand))
                .ConfigureAwait(false);
        }

        private PowerSnapshot LiveEntitySnapshot(uint entityId)
        {
            var scores = new Dictionary<ScoreId, int>();
            foreach (var score in AllScores)
                scores[score] = _statSystem.Get(entityId, score);
            return new PowerSnapshot(scores);
        }

        private static PowerSnapshot ItemSnapshot(ItemDataComponent item)
        {
            var scores = new Dictionary<ScoreId, int>();
            foreach (var bonus in item.StatBonuses)
                scores[bonus.TargetScore] = bonus.Magnitude;
            return new PowerSnapshot(scores);
        }
    }
}
