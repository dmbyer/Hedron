using Hedron.Core.Modules.Stats.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Stats
{
    public static class StatsModule
    {
        public static IServiceCollection AddStatsModule(this IServiceCollection services)
        {
            services.AddSingleton<IStatSystem, StatSystem>();
            services.AddSingleton<IStatRegistry, StatRegistry>();
            return services;
        }
    }
}
