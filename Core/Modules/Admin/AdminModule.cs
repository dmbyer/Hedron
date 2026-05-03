using Hedron.Core.Commands;
using Hedron.Core.Modules.Admin.Commands;
using Hedron.Core.Modules.Admin.Handlers;
using Hedron.Core.Modules.Admin.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Admin
{
    /// <summary>
    /// DI composition entry point for the Admin module: authorizer, the four admin commands,
    /// and the audit handler.
    /// </summary>
    /// <remarks>
    /// Subscriptions for <see cref="AdminAuditHandler"/> are registered against the event bus
    /// in <c>Server/Program.cs</c> alongside the other handler subscriptions, since the bus
    /// itself lives in <c>Server</c>.
    /// </remarks>
    public static class AdminModule
    {
        public static IServiceCollection AddAdminModule(this IServiceCollection services)
        {
            services.AddSingleton<IAdminAuthorizer, AdminAuthorizer>();

            services.AddSingleton<ICommand, SpawnCommand>();
            services.AddSingleton<ICommand, TeleportCommand>();
            services.AddSingleton<ICommand, DigCommand>();
            services.AddSingleton<ICommand, ReloadCommand>();

            services.AddSingleton<AdminAuditHandler>();
            return services;
        }
    }
}
