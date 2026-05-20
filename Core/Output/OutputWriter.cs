using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    /// <summary>
    /// Slice-3 implementation: stringifies each message and forwards via
    /// <see cref="ISession.SendLineAsync"/>. Slice 4 replaces this with a
    /// formatter-backed implementation.
    /// </summary>
    internal sealed class OutputWriter : IOutputWriter
    {
        private readonly ISession _session;

        public OutputWriter(ISession session) => _session = session;

        public Task WriteAsync(IOutputMessage message)
        {
            var text = message switch
            {
                PlainMessage m     => m.Text,
                HelpEntryMessage m => RenderEntry(m),
                HelpIndexMessage m => RenderIndex(m),
                _                  => message.ToString() ?? string.Empty,
            };
            return _session.SendLineAsync(text);
        }

        private static string RenderEntry(HelpEntryMessage m)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{m.Verb}]");
            sb.AppendLine(m.LongDescription);
            if (!string.IsNullOrEmpty(m.Usage))
            {
                sb.AppendLine();
                sb.Append($"Usage: {m.Usage}");
            }
            return sb.ToString();
        }

        private static string RenderIndex(HelpIndexMessage m)
        {
            var sb = new StringBuilder();
            var groups = m.Entries
                .GroupBy(e => e.Category)
                .OrderBy(g => (int)g.Key);

            var first = true;
            foreach (var group in groups)
            {
                if (!first) sb.AppendLine();
                first = false;
                sb.AppendLine($"=== {group.Key} ===");
                foreach (var entry in group.OrderBy(e => e.Verb))
                    sb.AppendLine($"  {entry.Verb,-14} {entry.ShortDescription}");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
