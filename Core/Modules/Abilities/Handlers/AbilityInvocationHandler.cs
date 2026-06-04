using System;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Abilities.Events;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Abilities.Handlers
{
    /// <summary>
    /// Renders narrative for <b>non-offensive</b> ability activations on
    /// <see cref="AbilityActivatedEvent"/>. Offensive abilities are narrated by
    /// <see cref="Hedron.Core.Modules.Combat.Handlers.AbilityStrikeHandler"/> via
    /// <see cref="Hedron.Core.Modules.Combat.Events.AbilityStrikeResolvedEvent"/> — this handler
    /// skips those to avoid duplicate output.
    /// </summary>
    public sealed class AbilityInvocationHandler : IEventHandler<AbilityActivatedEvent>
    {
        private readonly IAbilitySystem _abilitySystem;
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IBroadcastSystem _broadcast;
        private readonly EntityService _entityService;

        public int Priority => HandlerPriority.Notification;

        public AbilityInvocationHandler(
            IAbilitySystem abilitySystem,
            IAbilityRegistry abilityRegistry,
            IBroadcastSystem broadcast,
            EntityService entityService)
        {
            _abilitySystem = abilitySystem ?? throw new ArgumentNullException(nameof(abilitySystem));
            _abilityRegistry = abilityRegistry ?? throw new ArgumentNullException(nameof(abilityRegistry));
            _broadcast = broadcast ?? throw new ArgumentNullException(nameof(broadcast));
            _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        }

        public async Task HandleAsync(AbilityActivatedEvent @event)
        {
            // Offensive abilities: AbilityStrikeHandler owns narrative via AbilityStrikeResolvedEvent.
            if (_abilitySystem.IsOffensive(@event.AbilityId)) return;

            if (!_abilityRegistry.TryGet(@event.AbilityId, out var def)) return;

            var targetDisplay = @event.TargetEntityId.HasValue
                ? GetEntityName(@event.TargetEntityId.Value)
                : null;

            string actorLine = targetDisplay != null
                ? $"You {def.Name.ToLower()} {targetDisplay}."
                : $"You {def.Name.ToLower()}.";

            if (!_entityService.TryGet<LocationComponent>(@event.ActorEntityId, out var loc)) return;

            var attackerName = GetEntityName(@event.ActorEntityId);
            string observerLine = targetDisplay != null
                ? $"{attackerName} {def.Name.ToLower()}s {targetDisplay}."
                : $"{attackerName} {def.Name.ToLower()}s.";

            // Actor sees their own line.
            await _broadcast.SendToRoomAsync(
                loc.RoomEntityId,
                new PlainMessage(actorLine, OutputSeverity.System),
                entityId => entityId == @event.ActorEntityId)
                .ConfigureAwait(false);

            // Everyone else in the room sees the observer line.
            await _broadcast.SendToRoomAsync(
                loc.RoomEntityId,
                new PlainMessage(observerLine, OutputSeverity.System),
                entityId => entityId != @event.ActorEntityId)
                .ConfigureAwait(false);
        }

        private string GetEntityName(uint entityId)
        {
            if (_entityService.TryGet<PlayerComponent>(entityId, out var p)) return p.DisplayName;
            if (_entityService.TryGet<MobDataComponent>(entityId, out var m)) return m.Name;
            return "someone";
        }
    }
}
