using System.Collections.Generic;
using System.Text;
using Hedron.Core.Modules.Preferences.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Preferences
{
    /// <summary>
    /// The block written by a bare <c>config</c>: every registered preference with its current
    /// state and description. Self-formatting (like <c>AbilityDisplayMessage</c>) so adding a
    /// preference needs no formatter change.
    /// </summary>
    public sealed record PreferenceListMessage(IReadOnlyList<PreferenceState> States) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Info;

        public string Format()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<system>Settings</system>");

            if (States.Count == 0)
            {
                sb.Append("  (no configurable settings)");
                return sb.ToString();
            }

            foreach (var state in States)
            {
                sb.AppendLine(
                    $"  {state.Definition.Name,-20} {(state.Enabled ? "on " : "off")}  {state.Definition.Description}");
            }

            sb.Append("  Use 'config <name>' to flip a setting, or 'config <name> on|off' to set it.");
            return sb.ToString();
        }
    }
}
