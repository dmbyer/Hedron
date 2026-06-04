using Hedron.Core.Commands;
using Hedron.Core.Modules.Regeneration.Commands;
using Hedron.Core.Modules.Regeneration.Handlers;
using Hedron.Core.Modules.Regeneration.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Regeneration
{
    public static class RegenerationModule
    {
        public static IServiceCollection AddRegenerationModule(this IServiceCollection services)
        {
            services.AddSingleton<IRegenerationSystem, RegenerationSystem>();
            services.AddSingleton<RegenerationTickHandler>();
            services.AddSingleton<ICommand, RestCommand>();
            services.AddSingleton<ICommand, StandCommand>();
            return services;
        }
    }
}
