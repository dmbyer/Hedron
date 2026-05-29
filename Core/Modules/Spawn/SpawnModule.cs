using Hedron.Core.Modules.Spawn.Handlers;
using Hedron.Core.Modules.Spawn.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Spawn
{
    public static class SpawnModule
    {
        public static IServiceCollection AddSpawnModule(this IServiceCollection services)
        {
            services.AddSingleton<ISpawnSystem, SpawnSystem>();
            services.AddSingleton<SpawnSystem>(sp =>
                (SpawnSystem)sp.GetRequiredService<ISpawnSystem>());
            services.AddSingleton<ItemContextHandler>();
            return services;
        }
    }
}
