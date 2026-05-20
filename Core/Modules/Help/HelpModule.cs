using System;
using System.Collections.Generic;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Help.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Help
{
    /// <summary>
    /// DI composition entry point for the Help module: the <c>help</c> and <c>commands</c>
    /// commands. Depends on <c>IEnumerable&lt;ICommand&gt;</c> and
    /// <c>IAuthorizationChecker</c> being registered before this is resolved.
    /// </summary>
    public static class HelpModule
    {
        public static IServiceCollection AddHelpModule(this IServiceCollection services)
        {
            // Lazy breaks the IEnumerable<ICommand> → HelpCommand → IEnumerable<ICommand> cycle:
            // the factory is captured at construction but the collection is only resolved on first
            // command execution, by which point the DI container is fully built.
            services.AddSingleton(sp =>
                new Lazy<IEnumerable<ICommand>>(() => sp.GetServices<ICommand>()));

            services.AddSingleton<ICommand, HelpCommand>();
            services.AddSingleton<ICommand, CommandsCommand>();
            return services;
        }
    }
}
