using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection.Commands
{
    /// <summary>
    /// Admin/designer command <c>power [target]</c>. Resolves a runtime-in-world target — self
    /// (default), an item in inventory/room, or a mob in the room — to a <see cref="PowerSnapshot"/>
    /// and prints the computed power scalar and its classified <see cref="PowerBand"/> (Tier×Band).
    /// Blueprint-id/template resolution is deferred to the Blazor editor readout (resolved Q2 — see
    /// docs/features/progression/power-budget-system.md).
    ///
    /// <para>
    /// Live targets (self, mob) are measured through <see cref="IRelativePowerSystem.GetEffectivePower"/>
    /// — the same read <c>consider</c> and anti-grind use — and a mob target adds a relation line
    /// (ratio and label vs. the invoker) from <see cref="IRelativePowerSystem.Relate(uint, uint)"/>.
    /// Items stay on <see cref="IItemPowerProjectionSystem"/>.
    /// </para>
    /// </summary>
    public sealed class PowerCommand : ICommand
    {
        private readonly IPowerBudgetSystem _powerBudget;
        private readonly IRelativePowerSystem _relativePower;
        private readonly IItemSystem _itemSystem;
        private readonly IItemPowerProjectionSystem _itemProjection;
        private readonly ICombatSystem _combatSystem;
        private readonly EntityService _entityService;

        public string Name => "power";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Inspect a target's computed power and Tier×Band.";
        public string LongDescription =>
            "Resolves a runtime-in-world target — self (default), an item in your inventory/room, " +
            "or a mob in your room — to a score snapshot, then prints its computed power scalar " +
            "and classified (Tier, Band) alongside the authored (Tier, Band), when the target carries one. " +
            "Blueprint/template inspection lives in the Blazor editor.";
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
            IRelativePowerSystem relativePower,
            IItemSystem itemSystem,
            IItemPowerProjectionSystem itemProjection,
            ICombatSystem combatSystem,
            EntityService entityService)
        {
            _powerBudget = powerBudget;
            _relativePower = relativePower;
            _itemSystem = itemSystem;
            _itemProjection = itemProjection;
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
                await WriteReadout(context, "You", _relativePower.GetEffectivePower(context.InvokerEntityId),
                    authoredTier: null, authoredBand: null).ConfigureAwait(false);
                return;
            }

            _entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location);

            if (_itemSystem.TryFindItemInInventory(context.InvokerEntityId, target, out var invItemId) ||
                (location != null && _itemSystem.TryFindItemInRoom(location.RoomEntityId, target, out invItemId)))
            {
                var item = _entityService.Get<ItemDataComponent>(invItemId);
                await WriteReadout(context, item.Name, _powerBudget.Estimate(_itemProjection.Project(item), item.Tier),
                    item.Tier, item.Band).ConfigureAwait(false);
                return;
            }

            if (location != null && _combatSystem.TryFindTargetInRoom(location.RoomEntityId, target, out var mobId))
            {
                var mob = _entityService.Get<MobDataComponent>(mobId);
                var relation = _relativePower.Relate(context.InvokerEntityId, mobId);
                await WriteReadout(context, mob.Name, _relativePower.GetEffectivePower(mobId),
                    mob.Tier, mob.Band, relation).ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PlainMessage(
                "You don't see that here.", OutputSeverity.System, OutputCategory.System)).ConfigureAwait(false);
        }

        private async Task WriteReadout(
            CommandContext context, string label, int power, int? authoredTier, int? authoredBand,
            RelativePowerReading? relation = null)
        {
            var computed = _powerBudget.Classify(power);
            // Label → lower-case name is presentation; the ratio → label rule is the read's.
            // An unreadable relation carries its 0.0 ratio and a null label ("unreadable").
            await context.Output.WriteAsync(new PowerReadoutMessage(
                    label, power, computed, authoredTier, authoredBand,
                    RelationRatio: relation?.Ratio,
                    RelationLabel: relation?.Label?.ToString().ToLowerInvariant()))
                .ConfigureAwait(false);
        }
    }
}
