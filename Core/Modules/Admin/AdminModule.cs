using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.Admin.Commands;
using Hedron.Core.Modules.Admin.Handlers;
using Hedron.Core.Modules.Admin.Systems;
using Microsoft.Extensions.DependencyInjection;


namespace Hedron.Core.Modules.Admin
{
    /// <summary>
    /// DI composition entry point for the Admin module: authorizer, authorization checker,
    /// the four admin commands, and the audit handler.
    /// </summary>
    public static class AdminModule
    {
        public static IServiceCollection AddAdminModule(this IServiceCollection services)
        {
            services.AddSingleton<IAdminAuthorizer, AdminAuthorizer>();
            services.AddSingleton<IAuthorizationChecker, AuthorizationChecker>();
            services.AddSingleton<IRoomBuilderSystem, RoomBuilderSystem>();
            services.AddSingleton<IAreaBuilderSystem, AreaBuilderSystem>();

            services.AddSingleton<ICommand, SpawnCommand>();
            services.AddSingleton<ICommand, TeleportCommand>();
            services.AddSingleton<ICommand, DigCommand>();
            services.AddSingleton<ICommand, ReloadCommand>();
            services.AddSingleton<ICommand, SetCommand>();
            services.AddSingleton<ICommand, DefsCommand>();
            services.AddSingleton<ICommand, AreaCommand>();
            services.AddSingleton<ICommand, SetAreaCommand>();
            services.AddSingleton<ICommand, MkareaCommand>();
            services.AddSingleton<ICommand, ListEntitiesCommand>();

            services.AddSingleton<AdminAuditHandler>();
            return services;
        }
    }
}
