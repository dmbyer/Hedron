using System.Threading.Tasks;

namespace Hedron.Core.Output
{
    internal sealed class SessionBufferedOutputWriter : IOutputWriter
    {
        private readonly ISessionOutputBuffer _buffer;

        public SessionBufferedOutputWriter(ISessionOutputBuffer buffer)
        {
            _buffer = buffer;
        }

        public async Task WriteAsync(IOutputMessage message)
        {
            _buffer.Enqueue(message);
            if (CategoryFlushPolicy.GetPolicy(message.Category) == FlushPolicy.Immediate)
                await _buffer.FlushAsync().ConfigureAwait(false);
        }

        public Task FlushAsync() => _buffer.FlushAsync();
    }
}
