using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Events;
using Hedron.Core.Modules.Preferences.Events;
using Hedron.Core.Modules.Preferences.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Preferences.Commands
{
    /// <summary>
    /// Player verb <c>config</c> (alias <c>toggle</c>) — the single surface for every
    /// player-configurable setting.
    /// <list type="bullet">
    ///   <item><c>config</c> — list every preference and its current state.</item>
    ///   <item><c>config &lt;name&gt;</c> — flip that preference.</item>
    ///   <item><c>config &lt;name&gt; on|off</c> — set it explicitly.</item>
    /// </list>
    /// Initiator: reads through <see cref="IPreferenceSystem"/> (which publishes nothing, INV-5)
    /// and publishes <see cref="PreferenceChangedEvent"/> itself (INV-9).
    /// </summary>
    public sealed class ConfigCommand : ICommand
    {
        private static readonly string[] OnTokens = { "on", "yes", "true", "enable", "enabled", "1" };
        private static readonly string[] OffTokens = { "off", "no", "false", "disable", "disabled", "0" };

        private readonly IPreferenceSystem _preferenceSystem;
        private readonly IEventBus _eventBus;

        public string Name => "config";
        public IReadOnlyList<string> Aliases { get; } = new[] { "toggle" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public bool UsableWhileIncapacitated => true;
        public string ShortDescription => "List or change your settings.";
        public string LongDescription =>
            "Shows every configurable setting and whether it is on or off. " +
            "'config <name>' flips a setting; 'config <name> on' or 'config <name> off' sets it explicitly. " +
            "Setting names may be shortened to any unambiguous prefix. Your settings are saved with your character.";
        public string Usage => "config [<name> [on|off]]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("name", typeof(string), CommandArgumentKind.Token,
                Required: false, "Setting to change. Omit to list every setting."),
            new CommandArgument("state", typeof(string), CommandArgumentKind.Token,
                Required: false, "'on' or 'off'. Omit to flip the current value."),
        });

        public ConfigCommand(IPreferenceSystem preferenceSystem, IEventBus eventBus)
        {
            _preferenceSystem = preferenceSystem;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var entityId = context.InvokerEntityId;
            context.Args.TryGet<string>("name", out var name);

            if (string.IsNullOrWhiteSpace(name))
            {
                await context.Output.WriteAsync(
                    new PreferenceListMessage(_preferenceSystem.GetAll(entityId))).ConfigureAwait(false);
                return;
            }

            if (!PreferenceRegistry.TryResolve(name, out var preference))
            {
                var known = string.Join(", ", PreferenceRegistry.All.Select(d => d.Name));
                await context.Output.WriteAsync(new PlainMessage(
                    $"No setting matches '{name}'. Known settings: {known}.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            context.Args.TryGet<string>("state", out var state);

            bool enabled;
            if (string.IsNullOrWhiteSpace(state))
            {
                enabled = !_preferenceSystem.IsEnabled(entityId, preference);
            }
            else if (OnTokens.Contains(state.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                enabled = true;
            }
            else if (OffTokens.Contains(state.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                enabled = false;
            }
            else
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"'{state}' is not a valid state. Use 'on' or 'off', or omit it to flip the setting.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            _preferenceSystem.Set(entityId, preference, enabled);

            var definition = PreferenceRegistry.Get(preference);
            await context.Output.WriteAsync(new PlainMessage(
                $"{definition.Name} is now {(enabled ? "on" : "off")}.",
                OutputSeverity.System, OutputCategory.System)).ConfigureAwait(false);

            await _eventBus.PublishAsync(new PreferenceChangedEvent(entityId, preference, enabled))
                .ConfigureAwait(false);
        }
    }
}
