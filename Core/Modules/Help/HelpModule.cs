using System;
using System.Collections.Generic;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Help.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Help
{
    /// <summary>
    /// DI composition entry point for the Help module: the <c>help</c> and <c>commands</c>
    /// commands. Depends on <c>IEnumerable&lt;ICommand&gt;</c>, <c>IAuthorizationChecker</c>,
    /// <c>IVerbRegistry</c>, and <c>IAbilityRegistry</c> being registered before this is resolved.
    /// <para>
    /// Both <c>Lazy&lt;IEnumerable&lt;ICommand&gt;&gt;</c> and <c>Lazy&lt;IVerbRegistry&gt;</c>
    /// break the circular dependency that would otherwise exist because <c>HelpCommand</c> is
    /// itself an <c>ICommand</c> and <c>IVerbRegistry</c> is implemented by
    /// <c>CommandDispatcher</c>, which depends on all <c>ICommand</c> registrations.
    /// </para>
    /// <para>
    /// <c>IAbilityRegistry</c> is injected into <c>HelpCommand</c> so that
    /// <c>help kick</c> falls through to ability details when no command matches.
    /// <c>AddAbilitiesModule</c> must be called before <c>AddHelpModule</c> (both are in
    /// <c>Program.cs</c> — order there controls resolution order).
    /// </para>
    /// </summary>
    public static class HelpModule
    {
        public static IServiceCollection AddHelpModule(this IServiceCollection services)
        {
            // Lazy breaks the IEnumerable<ICommand> → HelpCommand → IEnumerable<ICommand> cycle.
            services.AddSingleton(sp =>
                new Lazy<IEnumerable<ICommand>>(() => sp.GetServices<ICommand>()));

            // Lazy breaks the HelpCommand → IVerbRegistry → CommandDispatcher → HelpCommand cycle.
            services.AddSingleton(sp =>
                new Lazy<IVerbRegistry>(() => sp.GetRequiredService<IVerbRegistry>()));

            services.AddSingleton<ICommand, HelpCommand>();
            services.AddSingleton<ICommand, CommandsCommand>();
            return services;
        }
    }
}
