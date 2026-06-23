using Hedron.Core.Commands;
using Hedron.Core.Modules.Economy.Commands;
using Hedron.Core.Modules.Economy.Handlers;
using Hedron.Core.Modules.Economy.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Economy
{
    /// <summary>
    /// DI composition entry-point for the Economy module.
    /// Call <see cref="AddEconomyModule"/> from <c>CompositionRoot.Register</c> so that
    /// <b>both</b> the gameplay server and the <c>Hedron.Web</c> content-authoring host compose
    /// the economy types — registering only in <c>Program.cs</c> would leave the Blazor host
    /// without <see cref="ICurrencyRegistry"/> and <see cref="IWalletSystem"/>.
    /// </summary>
    public static class EconomyModule
    {
        public static IServiceCollection AddEconomyModule(this IServiceCollection services)
        {
            services.AddSingleton<ICurrencyRegistry, CurrencyRegistry>();
            services.AddSingleton<IWalletSystem, WalletSystem>();
            services.AddSingleton<ICurrencyLootSystem, CurrencyLootSystem>();
            services.AddSingleton<CurrencyLootHandler>();
            services.AddSingleton<CurrencyAwardNarrationHandler>();
            services.AddSingleton<ICommand, SetwalletCommand>();
            return services;
        }
    }
}
