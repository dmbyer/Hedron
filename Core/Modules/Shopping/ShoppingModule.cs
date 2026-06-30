using Hedron.Core.Commands;
using Hedron.Core.Modules.Shopping.Commands;
using Hedron.Core.Modules.Shopping.Handlers;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Modules.Spawn.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Shopping
{
    /// <summary>
    /// DI composition entry-point for the Shopping module.
    /// Call <see cref="AddShoppingModule"/> from <c>CompositionRoot.Register</c> so that both the
    /// gameplay server and the <c>Hedron.Web</c> content-authoring host compose the shopping types.
    ///
    /// <para>
    /// WP-1 registers <see cref="ShopOptions"/> configuration and <see cref="ShopkeeperSpawnHandler"/>.
    /// WP-2 registers <see cref="IShopSystem"/>, <see cref="ShopInteractionHandler"/>,
    /// <see cref="ItemContextHandler"/> (shared, for buy/sell persistence transitions), and the
    /// trade-verb commands (<c>list</c>, <c>buy</c>, <c>sell</c>).
    /// WP-3 registers <see cref="ShopRestockTickHandler"/> and <see cref="ShopExpiryTickHandler"/>
    /// (both subscribed to <c>HeartbeatTickEvent</c> in <c>Server/Program.cs</c>).
    /// WP-3 also extracts <c>MobInRoomResolver</c> to <c>Core/Modules/Mobs/Resolvers/</c> (INV-19).
    /// </para>
    ///
    /// <para>
    /// <b>Caller responsibility:</b> <c>CompositionRoot.Register</c> must call
    /// <c>services.Configure&lt;ShopOptions&gt;(configuration.GetSection("Shop"))</c>
    /// because <see cref="IConfiguration"/> is not available inside a DI extension method.
    /// </para>
    /// </summary>
    public static class ShoppingModule
    {
        public static IServiceCollection AddShoppingModule(this IServiceCollection services)
        {
            // WP-1: register ShopkeeperSpawnHandler (subscribes to WorldContentReadyEvent in Program.cs).
            services.AddSingleton<ShopkeeperSpawnHandler>();

            // WP-2: domain system.
            services.AddSingleton<IShopSystem, ShopSystem>();

            // WP-2: handler for narration + ShopStockComponent cleanup on buy.
            // ItemContextHandler is already registered in SpawnModule; reused here via Program.cs subscription.
            services.AddSingleton<ShopInteractionHandler>();

            // WP-2: trade-verb commands.
            services.AddSingleton<ICommand, ListCommand>();
            services.AddSingleton<ICommand, BuyCommand>();
            services.AddSingleton<ICommand, SellCommand>();

            // WP-3: heartbeat sweep handlers (subscribed in Server/Program.cs).
            services.AddSingleton<ShopRestockTickHandler>();
            services.AddSingleton<ShopExpiryTickHandler>();

            return services;
        }
    }
}
