using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    /// <summary>
    /// Renders <see cref="IOutputMessage"/> shapes to ANSI-colored strings for telnet clients.
    /// Palette: four named semantic roles (<c>system</c>, <c>error</c>, <c>room-name</c>,
    /// <c>direction</c>).  Inline markers use XML-like syntax: <c>&lt;role&gt;text&lt;/role&gt;</c>.
    /// All color is stripped when <see cref="ISession.SupportsColor"/> is <c>false</c>.
    /// </summary>
    public sealed class TelnetOutputFormatter : IOutputFormatter
    {
        public string TransportKey => "telnet";

        // ANSI escape sequences for the four semantic roles.
        private const string Reset     = "\x1B[0m";
        private const string System    = "\x1B[96m";  // bright cyan
        private const string Error     = "\x1B[91m";  // bright red
        private const string RoomName  = "\x1B[93m";  // bright yellow
        private const string Direction = "\x1B[32m";  // green

        private static readonly Regex MarkerPattern = new(
            @"<(system|error|room-name|direction)>(.*?)</\1>",
            RegexOptions.Singleline | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        public string Format(IOutputMessage message, ISession session)
        {
            bool color = session.SupportsColor;
            return message switch
            {
                PlainMessage m            => FormatPlain(m, color),
                RoomDescriptionMessage m  => FormatRoom(m, color),
                MovementMessage m         => FormatMovement(m, color),
                HelpIndexMessage m        => FormatHelpIndex(m, color),
                HelpEntryMessage m        => FormatHelpEntry(m, color),
                _                         => message.ToString() ?? string.Empty,
            };
        }

        // ── Per-type renderers ────────────────────────────────────────────────

        private string FormatPlain(PlainMessage m, bool color)
        {
            var marked = m.Severity switch
            {
                OutputSeverity.Error        => $"<error>{m.Text}</error>",
                OutputSeverity.System       => $"<system>{m.Text}</system>",
                _                           => m.Text,
            };
            return ApplyColor(marked, color);
        }

        private string FormatRoom(RoomDescriptionMessage m, bool color)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(ApplyColor($"<room-name>{m.Name}</room-name>", color));
            sb.AppendLine(m.Description);

            var exitList = m.Exits.Count > 0
                ? string.Join(", ", m.Exits.Keys.Select(d =>
                    ApplyColor($"<direction>{d.ToString().ToLower()}</direction>", color)))
                : "none";
            sb.Append($"Exits: {exitList}");

            foreach (var occupant in m.OccupantNames)
                sb.AppendLine().Append($"{occupant} is here.");

            if (m.Items.Count > 0)
                sb.AppendLine().Append($"Items: {string.Join(", ", m.Items)}");

            return sb.ToString();
        }

        private string FormatMovement(MovementMessage m, bool color)
        {
            var text = m.Kind switch
            {
                MovementDirectionKind.Blocked => "You cannot go that way.",
                _                             => "You cannot go that way.",
            };
            return ApplyColor($"<system>{text}</system>", color);
        }

        private string FormatHelpIndex(HelpIndexMessage m, bool color)
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
                sb.AppendLine(ApplyColor($"<system>=== {group.Key} ===</system>", color));
                foreach (var entry in group.OrderBy(e => e.Verb))
                {
                    var label = entry.Aliases.Count > 0
                        ? $"{entry.Verb}  (aliases: {string.Join(", ", entry.Aliases)})"
                        : entry.Verb;
                    // Pad before colorizing so terminal column alignment is correct.
                    var paddedLabel = label.PadRight(22);
                    var coloredVerb = ApplyColor($"<room-name>{paddedLabel}</room-name>", color);
                    sb.AppendLine($"  {coloredVerb} {entry.ShortDescription}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private string FormatHelpEntry(HelpEntryMessage m, bool color)
        {
            var sb = new StringBuilder();
            var header = m.Aliases.Count > 0
                ? $"[{m.Verb}]  (aliases: {string.Join(", ", m.Aliases)})"
                : $"[{m.Verb}]";
            sb.AppendLine(ApplyColor($"<room-name>{header}</room-name>", color));
            sb.AppendLine(m.LongDescription);
            if (!string.IsNullOrEmpty(m.Usage))
            {
                sb.AppendLine();
                sb.Append($"Usage: {m.Usage}");
            }
            return sb.ToString();
        }

        // ── Color helpers ────────────────────────────────────────────────────

        private string ApplyColor(string text, bool color) =>
            color ? RenderMarkers(text) : StripMarkers(text);

        private static string RenderMarkers(string text) =>
            MarkerPattern.Replace(text, match =>
            {
                var ansi = match.Groups[1].Value switch
                {
                    "system"    => System,
                    "error"     => Error,
                    "room-name" => RoomName,
                    "direction" => Direction,
                    _           => string.Empty,
                };
                return $"{ansi}{match.Groups[2].Value}{Reset}";
            });

        private static string StripMarkers(string text) =>
            MarkerPattern.Replace(text, match => match.Groups[2].Value);
    }
}
