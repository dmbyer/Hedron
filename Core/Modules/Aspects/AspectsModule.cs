using Hedron.Core.Modules.Aspects.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Aspects
{
    public static class AspectsModule
    {
        public static IServiceCollection AddAspectsModule(this IServiceCollection services)
        {
            services.AddSingleton<IAspectRegistry, AspectRegistry>();
            services.AddSingleton<IAspectSystem, AspectSystem>();
            return services;
        }
    }
}
