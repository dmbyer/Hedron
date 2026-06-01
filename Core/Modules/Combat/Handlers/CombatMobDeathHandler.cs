using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Mobs.Events;

namespace Hedron.Core.Modules.Combat.Handlers
{
    /// <summary>
    /// Finalizes the mob death path: clears the player's combat state, publishes
    /// <see cref="MobDiedEvent"/> so <c>SpawnSystem</c> can mark the slot vacant, then
    /// destroys the mob entity. Priority 80 (<see cref="HandlerPriority.Notification"/>)
    /// — deliberately lower than <see cref="CombatHandler"/> (priority 20) so the death
    /// narrative is broadcast before the entity is destroyed.
    /// </summary>
    public sealed class CombatMobDeathHandler : IEventHandler<CombatEndedEvent>
    {
        private readonly EntityService _entityService;
        private readonly IEntityStateService _entityStateService;
        private readonly IEventBus _eventBus;

        public int Priority => HandlerPriority.Notification;

        public CombatMobDeathHandler(
            EntityService entityService,
            IEntityStateService entityStateService,
            IEventBus eventBus)
        {
            _entityService = entityService;
            _entityStateService = entityStateService;
            _eventBus = eventBus;
        }

        public async Task HandleAsync(CombatEndedEvent @event)
        {
            if (@event.Outcome != CombatEndOutcome.MobDied)
                return;

            _entityStateService.ExitState(@event.AttackerEntityId, EntityStateFlags.InCombat);

            // Publish MobDiedEvent while the entity is still live so SpawnSystem can inspect it.
            var blueprintId = _entityService.TryGet<BlueprintComponent>(@event.DefenderEntityId, out var bp)
                ? bp.BlueprintId
                : string.Empty;
            await _eventBus.PublishAsync(new MobDiedEvent(
                    @event.DefenderEntityId,
                    blueprintId,
                    KillerEntityId: @event.AttackerEntityId))
                .ConfigureAwait(false);

            _entityService.DestroyEntity(@event.DefenderEntityId);
        }
    }
}
