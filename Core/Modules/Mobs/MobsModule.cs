using Hedron.Core.Commands;
using Hedron.Core.Modules.Mobs.Commands;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Mobs
{
    public static class MobsModule
    {
        public static IServiceCollection AddMobsModule(this IServiceCollection services)
        {
            services.AddSingleton<IMobBuilderSystem, MobBuilderSystem>();
            services.AddSingleton<IMobContentWriter, MobContentWriter>();
            services.AddSingleton<IMobPowerProjectionSystem, MobPowerProjectionSystem>();
            services.AddSingleton<ICommand, MkMobCommand>();
            services.AddSingleton<ICommand, SetMobCommand>();
            services.AddSingleton<ITemplateDeserializer, MobTemplateDeserializer>();
            return services;
        }
    }
}
