using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Time.Events;

namespace Hedron.Core.Modules.Attributes.Handlers
{
    /// <summary>
    /// Writes each pool's clamp back to storage once per heartbeat: any current above its effective
    /// cap (a <c>&lt;Pool&gt;Max</c> buff expired, gear came off, a passive was lost) is lowered to the cap.
    /// Reads are already clamped, so this only keeps stored overflow from outliving a tick — it is what
    /// stops a removed-then-reapplied max bonus from refilling current for free.
    /// Publishes nothing and never consults the death system: it only lowers toward a cap of at least 1,
    /// so it can never cross an HP threshold (INV-5).
    /// </summary>
    /// <remarks>
    /// Priority <c>Domain + 10</c>: a mechanical follow-up of the Domain tier with no observer of its
    /// own — it runs after every p=20 pool writer (effect expiry, combat, regen, bleed-out) so this
    /// tick's drops are written back in the same tick, and before Notification/OutputFlush.
    /// </remarks>
    public sealed class PoolReconcileTickHandler : IEventHandler<HeartbeatTickEvent>
    {
        private readonly IAttributeSystem _attributeSystem;

        public int Priority => HandlerPriority.Domain + 10;

        public PoolReconcileTickHandler(IAttributeSystem attributeSystem)
        {
            _attributeSystem = attributeSystem;
        }

        public Task HandleAsync(HeartbeatTickEvent @event)
        {
            _attributeSystem.ReconcileAllPools();
            return Task.CompletedTask;
        }
    }
}
