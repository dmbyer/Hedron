using Hedron.Core.Commands;
using Hedron.Core.Modules.Effects.Commands;
using Hedron.Core.Modules.Effects.Handlers;
using Hedron.Core.Modules.Effects.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Effects
{
    public static class EffectsModule
    {
        public static IServiceCollection AddEffectsModule(this IServiceCollection services)
        {
            services.AddSingleton<IEffectSystem, EffectSystem>();
            services.AddSingleton<IEffectRegistry, EffectRegistry>();
            services.AddSingleton<EffectTickHandler>();
            services.AddSingleton<ICommand, AffectCommand>();
            services.AddSingleton<ICommand, AffectsCommand>();
            return services;
        }
    }
}
