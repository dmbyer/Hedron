using Hedron.Core.ECS;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Systems;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 5 / DI-smoke — verifies that <see cref="EconomyModule.AddEconomyModule"/> registers
    /// and resolves all Economy types correctly (INV-DI, exit criterion for WP-1).
    /// </summary>
    public sealed class EconomyModuleDiTests
    {
        private static ServiceProvider BuildProvider()
        {
            var services = new ServiceCollection();
            // EntityService is required by WalletSystem.
            var ecs = new EntityService();
            services.AddSingleton(ecs);
            services.AddEconomyModule();
            return services.BuildServiceProvider();
        }

        [Fact]
        public void AddEconomyModule_resolves_ICurrencyRegistry()
        {
            using var provider = BuildProvider();
            var registry = provider.GetService<ICurrencyRegistry>();
            Assert.NotNull(registry);
        }

        [Fact]
        public void AddEconomyModule_resolves_IWalletSystem()
        {
            using var provider = BuildProvider();
            var walletSystem = provider.GetService<IWalletSystem>();
            Assert.NotNull(walletSystem);
        }

        [Fact]
        public void AddEconomyModule_ICurrencyRegistry_is_singleton()
        {
            using var provider = BuildProvider();
            var a = provider.GetRequiredService<ICurrencyRegistry>();
            var b = provider.GetRequiredService<ICurrencyRegistry>();
            Assert.Same(a, b);
        }

        [Fact]
        public void AddEconomyModule_IWalletSystem_is_singleton()
        {
            using var provider = BuildProvider();
            var a = provider.GetRequiredService<IWalletSystem>();
            var b = provider.GetRequiredService<IWalletSystem>();
            Assert.Same(a, b);
        }
    }
}
