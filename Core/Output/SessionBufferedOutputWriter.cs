using System.Threading.Tasks;

namespace Hedron.Core.Output
{
    internal sealed class SessionBufferedOutputWriter : IOutputWriter
    {
        private readonly ISessionOutputBuffer _buffer;
        private bool _deferNextFlush;

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

        public void DeferFlush() => _deferNextFlush = true;

        public Task FlushAsync()
        {
            if (_deferNextFlush) { _deferNextFlush = false; return Task.CompletedTask; }
            return _buffer.FlushAsync();
        }
    }
}
