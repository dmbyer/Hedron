using Hedron.Core.Commands;
using Hedron.Core.Modules.Death.Commands;
using Hedron.Core.Modules.Death.Handlers;
using Hedron.Core.Modules.Death.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Death
{
    /// <summary>
    /// DI composition entry point for the Death module.
    /// </summary>
    public static class DeathModule
    {
        public static IServiceCollection AddDeathModule(this IServiceCollection services)
        {
            services.AddSingleton<IDeathSystem, DeathSystem>();
            services.AddSingleton<DeathTickHandler>();
            services.AddSingleton<PlayerDeathHandler>();
            services.AddSingleton<DeathNarrationHandler>();
            services.AddSingleton<ICommand, SetRespawnCommand>();
            return services;
        }
    }
}
