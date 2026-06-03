using Hedron.Core.Commands;
using Hedron.Core.Modules.Abilities.Commands;
using Hedron.Core.Modules.Abilities.Handlers;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Effects.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Abilities
{
    public static class AbilitiesModule
    {
        public static IServiceCollection AddAbilitiesModule(this IServiceCollection services)
        {
            services.AddSingleton<IAbilityRegistry, AbilityRegistry>();
            services.AddSingleton<IAbilitySystem, AbilitySystem>();
            services.AddSingleton<IEffectContributor, AbilityEffectContributor>();
            services.AddSingleton<AbilityCooldownTickHandler>();
            services.AddSingleton<ICommand, TeachCommand>();
            services.AddSingleton<ICommand, UseAbilityCommand>();
            services.AddSingleton<ICommand, AbilitiesCommand>();
            return services;
        }
    }
}
