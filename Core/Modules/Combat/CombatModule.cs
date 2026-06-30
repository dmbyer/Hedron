using Hedron.Core.Commands;
using Hedron.Core.Modules.Combat.Commands;
using Hedron.Core.Modules.Combat.Handlers;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Mobs.Resolvers;
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

            // Shared resolver — stateless singleton; injected into combat + shopping commands.
            // Lives in Core/Modules/Mobs/Resolvers/ (INV-19 extraction: three consumers).
            services.AddSingleton<MobInRoomResolver>();

            services.AddSingleton<ICommand, KillCommand>();
            services.AddSingleton<ICommand, FleeCommand>();
            return services;
        }
    }
}
