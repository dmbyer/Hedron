using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.Regeneration.Systems;
using Hedron.Core.Modules.Time.Events;

namespace Hedron.Core.Modules.Regeneration.Handlers
{
    /// <summary>
    /// Drives the baseline regeneration sweep on each heartbeat tick.
    /// Publishes nothing — regeneration is a closed mechanical sweep with no downstream chain (INV-10).
    /// </summary>
    public sealed class RegenerationTickHandler : IEventHandler<HeartbeatTickEvent>
    {
        private readonly IRegenerationSystem _regenSystem;

        public int Priority => HandlerPriority.Domain;

        public RegenerationTickHandler(IRegenerationSystem regenSystem)
        {
            _regenSystem = regenSystem;
        }

        public Task HandleAsync(HeartbeatTickEvent @event)
        {
            _regenSystem.ApplyTickRegen(@event.TickId);
            return Task.CompletedTask;
        }
    }
}
