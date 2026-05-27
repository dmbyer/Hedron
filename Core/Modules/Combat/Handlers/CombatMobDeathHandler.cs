using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.EntityState.Systems;

namespace Hedron.Core.Modules.Combat.Handlers
{
    /// <summary>
    /// Finalizes the mob death path: clears the player's combat state, frees the blueprint slot
    /// (INV-21), and destroys the mob entity. Priority 80 (<see cref="HandlerPriority.Notification"/>)
    /// — deliberately lower than <see cref="CombatHandler"/> (priority 20) so the death narrative
    /// is broadcast before the entity is destroyed.
    /// </summary>
    public sealed class CombatMobDeathHandler : IEventHandler<CombatEndedEvent>
    {
        private readonly EntityService _entityService;
        private readonly IEntityStateService _entityStateService;

        public int Priority => HandlerPriority.Notification;

        public CombatMobDeathHandler(EntityService entityService, IEntityStateService entityStateService)
        {
            _entityService = entityService;
            _entityStateService = entityStateService;
        }

        public Task HandleAsync(CombatEndedEvent @event)
        {
            if (@event.Outcome != CombatEndOutcome.MobDied)
                return Task.CompletedTask;

            _entityStateService.ExitState(@event.AttackerEntityId, EntityStateFlags.InCombat);

            // Clear blueprint slot so WorldContentLoader re-seeds on next startup/reload (INV-21).
            _entityService.RemoveComponent<BlueprintComponent>(@event.DefenderEntityId);

            _entityService.DestroyEntity(@event.DefenderEntityId);

            return Task.CompletedTask;
        }
    }
}
