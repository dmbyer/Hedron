using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    public sealed class OutputFormatterRegistry : IOutputFormatterRegistry
    {
        private readonly Dictionary<string, IOutputFormatter> _byKey;
        private readonly IOutputFormatter _fallback;

        public OutputFormatterRegistry(IEnumerable<IOutputFormatter> formatters)
        {
            var list = formatters.ToList();
            _byKey = list.ToDictionary(f => f.TransportKey);
            _fallback = list[0];
        }

        public IOutputFormatter Resolve(ISession session) =>
            _byKey.TryGetValue(session.TransportKey, out var f) ? f : _fallback;
    }
}
