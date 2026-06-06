using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.Time.Events;
using Hedron.Core.Output;

namespace Hedron.Core.Handlers
{
    /// <summary>
    /// Flushes all session output buffers at the end of each heartbeat tick.
    /// Priority <see cref="HandlerPriority.OutputFlush"/> (85) — runs after all
    /// output-producing handlers (<see cref="HandlerPriority.Notification"/> = 80) and
    /// before persistence (90). Each session with pending output is drained atomically and
    /// one freshly-composed prompt is appended per session.
    /// </summary>
    public sealed class OutputFlushTickHandler : IEventHandler<HeartbeatTickEvent>
    {
        private readonly ISessionBufferRegistry _bufferRegistry;

        public int Priority => HandlerPriority.OutputFlush;

        public OutputFlushTickHandler(ISessionBufferRegistry bufferRegistry)
        {
            _bufferRegistry = bufferRegistry;
        }

        public Task HandleAsync(HeartbeatTickEvent @event) =>
            _bufferRegistry.FlushAllPendingAsync();
    }
}
