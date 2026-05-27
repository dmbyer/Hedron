using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Combat.Handlers
{
    /// <summary>
    /// Pure output fan-out for all combat events. Does not call systems or mutate state.
    /// Priority 20 (<see cref="HandlerPriority.Domain"/>) — runs before <see cref="CombatMobDeathHandler"/>
    /// (priority 80) so death narrative is broadcast before entity destruction.
    /// </summary>
    public sealed class CombatHandler :
        IEventHandler<CombatStartedEvent>,
        IEventHandler<CombatRoundEvent>,
        IEventHandler<CombatEndedEvent>
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;

        public int Priority => HandlerPriority.Domain;

        public CombatHandler(EntityService entityService, IBroadcastSystem broadcast)
        {
            _entityService = entityService;
            _broadcast = broadcast;
        }

        public async Task HandleAsync(CombatStartedEvent @event)
        {
            var attackerName = GetPlayerName(@event.AttackerEntityId);
            var defenderName = GetMobName(@event.DefenderEntityId);

            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage($"You attack {defenderName}!", OutputSeverity.System),
                entityId => entityId == @event.AttackerEntityId)
                .ConfigureAwait(false);

            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage($"{attackerName} attacks {defenderName}!", OutputSeverity.System),
                entityId => entityId != @event.AttackerEntityId)
                .ConfigureAwait(false);
        }

        public async Task HandleAsync(CombatRoundEvent @event)
        {
            var result = @event.Result;

            if (!result.AttackerHit)
            {
                var missText = GetCombatantLabel(@event.AttackerEntityId) == "player"
                    ? $"You miss {GetMobOrPlayerName(@event.DefenderEntityId)}."
                    : $"{GetMobOrPlayerName(@event.AttackerEntityId)} misses you.";

                await _broadcast.SendToRoomAsync(
                    @event.RoomEntityId,
                    new PlainMessage(missText, OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            var attackerName = GetMobOrPlayerName(@event.AttackerEntityId);
            var defenderName = GetMobOrPlayerName(@event.DefenderEntityId);
            var isAttackerPlayer = _entityService.HasComponent<PlayerComponent>(@event.AttackerEntityId);

            // Personal hit message to attacker if player.
            if (isAttackerPlayer)
            {
                await _broadcast.SendToRoomAsync(
                    @event.RoomEntityId,
                    new PlainMessage($"You hit {defenderName} for {result.DamageDealt} damage.", OutputSeverity.System),
                    entityId => entityId == @event.AttackerEntityId)
                    .ConfigureAwait(false);
            }

            // Observer message (and defender's perspective if they're a player).
            var observerText = isAttackerPlayer
                ? $"{attackerName} hits {defenderName} for {result.DamageDealt} damage."
                : $"{attackerName} hits you for {result.DamageDealt} damage.";

            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage(observerText, OutputSeverity.System),
                entityId => entityId != @event.AttackerEntityId)
                .ConfigureAwait(false);
        }

        public async Task HandleAsync(CombatEndedEvent @event)
        {
            switch (@event.Outcome)
            {
                case CombatEndOutcome.MobDied:
                    var slainName = @event.DefenderName ?? "the creature";
                    await _broadcast.SendToRoomAsync(
                        @event.RoomEntityId,
                        new PlainMessage($"You have slain {slainName}!", OutputSeverity.System),
                        entityId => entityId == @event.AttackerEntityId)
                        .ConfigureAwait(false);
                    await _broadcast.SendToRoomAsync(
                        @event.RoomEntityId,
                        new PlainMessage($"{GetPlayerName(@event.AttackerEntityId)} has slain {slainName}!", OutputSeverity.System),
                        entityId => entityId != @event.AttackerEntityId)
                        .ConfigureAwait(false);
                    break;

                case CombatEndOutcome.PlayerFled:
                    var playerName = GetPlayerName(@event.AttackerEntityId);
                    await _broadcast.SendToRoomAsync(
                        @event.RoomEntityId,
                        new PlainMessage("You flee from combat!", OutputSeverity.System),
                        entityId => entityId == @event.AttackerEntityId)
                        .ConfigureAwait(false);
                    await _broadcast.SendToRoomAsync(
                        @event.RoomEntityId,
                        new PlainMessage($"{playerName} flees from combat!", OutputSeverity.System),
                        entityId => entityId != @event.AttackerEntityId)
                        .ConfigureAwait(false);
                    break;

                case CombatEndOutcome.PlayerIncapacitated:
                    await _broadcast.SendToRoomAsync(
                        @event.RoomEntityId,
                        new PlainMessage("You have been beaten unconscious!", OutputSeverity.System),
                        entityId => entityId == @event.DefenderEntityId)
                        .ConfigureAwait(false);
                    await _broadcast.SendToRoomAsync(
                        @event.RoomEntityId,
                        new PlainMessage($"{GetPlayerName(@event.DefenderEntityId)} has been beaten unconscious!", OutputSeverity.System),
                        entityId => entityId != @event.DefenderEntityId)
                        .ConfigureAwait(false);
                    break;
            }
        }

        private string GetPlayerName(uint entityId) =>
            _entityService.TryGet<PlayerComponent>(entityId, out var p) ? p.DisplayName : "Someone";

        private string GetMobName(uint entityId) =>
            _entityService.TryGet<MobDataComponent>(entityId, out var m) ? m.Name : "something";

        private string GetMobOrPlayerName(uint entityId)
        {
            if (_entityService.TryGet<PlayerComponent>(entityId, out var p))
                return p.DisplayName;
            if (_entityService.TryGet<MobDataComponent>(entityId, out var m))
                return m.Name;
            return "something";
        }

        private string GetCombatantLabel(uint entityId) =>
            _entityService.HasComponent<PlayerComponent>(entityId) ? "player" : "mob";
    }
}
