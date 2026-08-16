using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Tests.Harness
{
    /// <summary>
    /// Captures <see cref="IBroadcastSystem"/> traffic without a session/output stack, so handler
    /// tests can assert <b>that</b> a line was written and to whom without asserting its prose
    /// (presentation is deliberately not pinned — see the slice's Test plan).
    /// </summary>
    public sealed class RecordingBroadcastSystem : IBroadcastSystem
    {
        /// <summary>Every <see cref="SendToEntityAsync"/> call, in order.</summary>
        public List<(uint EntityId, IOutputMessage Message)> ToEntity { get; } = new();

        /// <summary>Every <see cref="SendToRoomAsync"/> call, in order.</summary>
        public List<(uint RoomEntityId, IOutputMessage Message)> ToRoom { get; } = new();

        /// <summary>Every <see cref="SendToAllAsync"/> call, in order.</summary>
        public List<IOutputMessage> ToAll { get; } = new();

        public Task SendToRoomAsync(uint roomEntityId, IOutputMessage message, Func<uint, bool>? audienceFilter = null)
        {
            ToRoom.Add((roomEntityId, message));
            return Task.CompletedTask;
        }

        public Task SendToEntityAsync(uint playerEntityId, IOutputMessage message)
        {
            ToEntity.Add((playerEntityId, message));
            return Task.CompletedTask;
        }

        public Task SendToAllAsync(IOutputMessage message)
        {
            ToAll.Add(message);
            return Task.CompletedTask;
        }

        public Task SendRoomDescriptionAsync(uint playerEntityId, uint roomEntityId)
            => Task.CompletedTask;
    }
}
