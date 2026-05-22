using System.Threading.Tasks;
using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    /// <summary>
    /// Formatter-backed implementation of <see cref="IOutputWriter"/>.
    /// Resolves the session's transport formatter via <see cref="IOutputFormatterRegistry"/>,
    /// renders the typed message, and emits one line to the session.
    /// </summary>
    internal sealed class OutputWriter : IOutputWriter
    {
        private readonly ISession _session;
        private readonly IOutputFormatterRegistry _registry;

        public OutputWriter(ISession session, IOutputFormatterRegistry registry)
        {
            _session = session;
            _registry = registry;
        }

        public Task WriteAsync(IOutputMessage message)
        {
            var formatter = _registry.Resolve(_session);
            var rendered = formatter.Format(message, _session);
            return _session.SendLineAsync(rendered);
        }
    }
}
