using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    /// <summary>Creates <see cref="SessionBufferedOutputWriter"/> instances bound to a session.</summary>
    public sealed class OutputWriterFactory : IOutputWriterFactory
    {
        private readonly ISessionBufferRegistry _bufferRegistry;

        public OutputWriterFactory(ISessionBufferRegistry bufferRegistry)
            => _bufferRegistry = bufferRegistry;

        public IOutputWriter Create(ISession session)
        {
            var buffer = _bufferRegistry.GetOrCreate(session);
            return new SessionBufferedOutputWriter(buffer);
        }
    }
}
