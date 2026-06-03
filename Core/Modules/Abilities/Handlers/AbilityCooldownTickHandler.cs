using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Time.Events;

namespace Hedron.Core.Modules.Abilities.Handlers
{
    public sealed class AbilityCooldownTickHandler : IEventHandler<HeartbeatTickEvent>
    {
        private readonly IAbilitySystem _abilitySystem;
        public int Priority => HandlerPriority.Domain;

        public AbilityCooldownTickHandler(IAbilitySystem abilitySystem) => _abilitySystem = abilitySystem;

        public Task HandleAsync(HeartbeatTickEvent @event)
        {
            _abilitySystem.AdvanceCooldowns(@event.Elapsed);
            return Task.CompletedTask;
        }
    }
}
