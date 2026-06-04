using Hedron.Core.Commands;
using Hedron.Core.Modules.Abilities.Commands;
using Hedron.Core.Modules.Abilities.Handlers;
using Hedron.Core.Modules.Abilities.Resolvers;
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

            // Three discoverable list commands (replaces old single AbilitiesCommand with aliases).
            services.AddSingleton<ICommand, AbilitiesCommand>();
            services.AddSingleton<ICommand, SkillsCommand>();
            services.AddSingleton<ICommand, SpellsCommand>();

            // WP-2: ability verb resolver and skill invocation pipeline
            services.AddSingleton<IAbilityVerbResolver, AbilityVerbResolver>();
            services.AddSingleton<AbilityInvocationPipeline>();
            services.AddSingleton<SkillInvocationCommand>();
            services.AddSingleton<AbilityInvocationHandler>();

            // WP-3: spell resolver and cast command
            services.AddSingleton<KnownSpellResolver>();
            services.AddSingleton<ICommand, CastCommand>();

            return services;
        }
    }
}
