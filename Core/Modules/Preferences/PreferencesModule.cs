using Hedron.Core.Commands;
using Hedron.Core.Modules.Preferences.Commands;
using Hedron.Core.Modules.Preferences.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Preferences
{
    /// <summary>
    /// DI composition entry-point for the Preferences module — per-character configurable
    /// settings and the <c>config</c> verb that reads and writes them.
    ///
    /// <para>
    /// Its own module rather than a corner of <c>Modules/Session/</c>: preferences are
    /// per-<b>character</b> persistent state, not per-connection, and <c>Session</c> has no module
    /// entry point (its handler is wired directly in <c>CompositionRoot</c>).
    /// </para>
    /// </summary>
    public static class PreferencesModule
    {
        public static IServiceCollection AddPreferencesModule(this IServiceCollection services)
        {
            services.AddSingleton<IPreferenceSystem, PreferenceSystem>();
            services.AddSingleton<ICommand, ConfigCommand>();
            return services;
        }
    }
}
