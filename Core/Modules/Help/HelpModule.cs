using System;
using System.Collections.Generic;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Help.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Help
{
    /// <summary>
    /// DI composition entry point for the Help module: the <c>help</c> and <c>commands</c>
    /// commands. Depends on <c>IEnumerable&lt;ICommand&gt;</c>, <c>IAuthorizationChecker</c>,
    /// and <c>IVerbRegistry</c> being registered before this is resolved.
    /// <para>
    /// Both <c>Lazy&lt;IEnumerable&lt;ICommand&gt;&gt;</c> and <c>Lazy&lt;IVerbRegistry&gt;</c>
    /// break the circular dependency that would otherwise exist because <c>HelpCommand</c> is
    /// itself an <c>ICommand</c> and <c>IVerbRegistry</c> is implemented by
    /// <c>CommandDispatcher</c>, which depends on all <c>ICommand</c> registrations.
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
