using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Xunit;

namespace Hedron.Tests.Harness
{
    /// <summary>
    /// Stub <see cref="ISession"/> that carries a stable entity id.
    /// </summary>
    public sealed class StubSession : ISession
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public uint PlayerEntityId { get; }
        public string TransportKey => "stub";
        public bool SupportsColor => false;

        public StubSession(uint playerEntityId = 0)
        {
            PlayerEntityId = playerEntityId;
        }

        public Task SendLineAsync(string text) => Task.CompletedTask;
    }

    /// <summary>
    /// Records every <see cref="IOutputMessage"/> written via <see cref="IOutputWriter"/>,
    /// paired with the recipient entity id.
    /// No assertion on prose strings — tests assert on the concrete message type and audience.
    /// </summary>
    public sealed class RecordingOutputWriter : IOutputWriter
    {
        private readonly uint _recipientEntityId;
        private readonly List<(Type MessageType, uint RecipientEntityId, IOutputMessage Message)> _captures;

        public RecordingOutputWriter(
            uint recipientEntityId,
            List<(Type, uint, IOutputMessage)> captures)
        {
            _recipientEntityId = recipientEntityId;
            _captures = captures;
        }

        public Task WriteAsync(IOutputMessage message)
        {
            _captures.Add((message.GetType(), _recipientEntityId, message));
            return Task.CompletedTask;
        }

        public Task FlushAsync() => Task.CompletedTask;
        public void DeferFlush() { }
    }

    /// <summary>
    /// Collects all output written to any recipient, with helpers to assert on type and audience.
    /// </summary>
    public sealed class RecordingOutput : IOutputWriterFactory
    {
        private readonly List<(Type MessageType, uint RecipientEntityId, IOutputMessage Message)> _all = new();

        /// <summary>All captured records, in write order.</summary>
        public IReadOnlyList<(Type MessageType, uint RecipientEntityId, IOutputMessage Message)> All
            => _all;

        /// <inheritdoc/>
        public IOutputWriter Create(ISession session)
            => new RecordingOutputWriter(session.PlayerEntityId, _all);

        /// <summary>
        /// Returns a writer bound to a given entity id without needing a full <see cref="ISession"/>.
        /// </summary>
        public IOutputWriter WriterFor(uint entityId)
            => new RecordingOutputWriter(entityId, _all);

        /// <summary>
        /// Returns true if any message of type <typeparamref name="TMessage"/> was sent to
        /// <paramref name="recipientEntityId"/>.
        /// </summary>
        public bool HasMessage<TMessage>(uint recipientEntityId) where TMessage : IOutputMessage
            => _all.Any(r => r.MessageType == typeof(TMessage) && r.RecipientEntityId == recipientEntityId);

        /// <summary>
        /// Asserts that at least one message of type <typeparamref name="TMessage"/> was sent to
        /// <paramref name="recipientEntityId"/>.
        /// </summary>
        public void AssertMessage<TMessage>(uint recipientEntityId) where TMessage : IOutputMessage
        {
            Assert.True(
                HasMessage<TMessage>(recipientEntityId),
                $"Expected at least one {typeof(TMessage).Name} sent to entity {recipientEntityId}.");
        }
    }

    // ── Self-test ────────────────────────────────────────────────────────────────

    public sealed class RecordingOutputTests
    {
        private sealed class InfoMessage : IOutputMessage
        {
            public OutputCategory Category => OutputCategory.Info;
        }

        private sealed class CombatMessage : IOutputMessage
        {
            public OutputCategory Category => OutputCategory.Combat;
        }

        [Fact]
        public async Task WriteAsync_records_message_type_and_recipient()
        {
            var output = new RecordingOutput();
            var writer = output.WriterFor(entityId: 7);

            await writer.WriteAsync(new InfoMessage());

            Assert.Single(output.All);
            Assert.Equal(typeof(InfoMessage), output.All[0].MessageType);
            Assert.Equal(7u, output.All[0].RecipientEntityId);
        }

        [Fact]
        public async Task HasMessage_returns_true_when_type_and_recipient_match()
        {
            var output = new RecordingOutput();
            var writer = output.WriterFor(entityId: 3);
            await writer.WriteAsync(new CombatMessage());

            Assert.True(output.HasMessage<CombatMessage>(3u));
            Assert.False(output.HasMessage<InfoMessage>(3u));
            Assert.False(output.HasMessage<CombatMessage>(99u));
        }

        [Fact]
        public async Task Create_binds_writer_to_session_entity_id()
        {
            var output = new RecordingOutput();
            var session = new StubSession(playerEntityId: 55);
            var writer = output.Create(session);
            await writer.WriteAsync(new InfoMessage());

            Assert.True(output.HasMessage<InfoMessage>(55u));
        }
    }
}
