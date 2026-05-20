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
            services.AddSingleton<ICommand, HelpCommand>();
            services.AddSingleton<ICommand, CommandsCommand>();
            return services;
        }
    }
}
