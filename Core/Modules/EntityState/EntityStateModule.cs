using Hedron.Core.Modules.EntityState.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.EntityState
{
    public static class EntityStateModule
    {
        public static IServiceCollection AddEntityStateModule(this IServiceCollection services)
        {
            services.AddSingleton<IEntityStateService, EntityStateService>();
            return services;
        }
    }
}
