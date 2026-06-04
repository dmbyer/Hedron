using Hedron.Core.Commands;
using Hedron.Core.Modules.Combat.Commands;
using Hedron.Core.Modules.Combat.Handlers;
using Hedron.Core.Modules.Combat.Resolvers;
using Hedron.Core.Modules.Combat.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Combat
{
    public static class CombatModule
    {
        public static IServiceCollection AddCombatModule(this IServiceCollection services)
        {
            services.AddSingleton<ICombatSystem, CombatSystem>();
            services.AddSingleton<CombatTickHandler>();
            services.AddSingleton<CombatHandler>();
            services.AddSingleton<CombatMobDeathHandler>();
            services.AddSingleton<AbilityStrikeHandler>();

            // Resolver — stateless singleton; injected into ability-invocation commands via constructor.
            services.AddSingleton<MobInRoomResolver>();

            services.AddSingleton<ICommand, KillCommand>();
            services.AddSingleton<ICommand, FleeCommand>();
            return services;
        }
    }
}
