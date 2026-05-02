using Hedron.Core;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Chat.Commands;
using Hedron.Core.Modules.Chat.Events;
using Hedron.Core.Modules.Chat.Handlers;
using Hedron.Core.Modules.Movement.Commands;
using Hedron.Core.Modules.Movement.Events;
using Hedron.Core.Modules.Movement.Handlers;
using Hedron.Core.Modules.Movement.Systems;
using Hedron.Core.Modules.Persistence;
using Hedron.Core.Modules.Session.Events;
using Hedron.Core.Modules.Session.Handlers;
using Hedron.Core.Modules.World.Commands;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;
using Hedron.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hedron.Server;

/// <summary>
/// Composition root. Phase 2 steps 1–9 are wired here; step 10 is the live smoke test.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                // ECS world — singleton, shared between the static EcsManager bridge and
                // any DI-injected consumer.
                var world = new EntityService();
                EcsManager.SetWorld(world);
                services.AddSingleton(world);

                services.AddSingleton<IEventBus, EventBus>();
                services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
                services.AddSingleton<ISessionManager, SessionManager>();

                // Systems
                services.AddSingleton<IBroadcastSystem, BroadcastSystem>();
                services.AddSingleton<IMovementSystem, MovementSystem>();

                // World configuration — populated by the world bootstrap before RunAsync
                var worldConfig = new WorldConfiguration();
                services.AddSingleton(worldConfig);

                // Handlers
                services.AddSingleton<PlayerSessionHandler>();
                services.AddSingleton<PlayerMovedHandler>();
                services.AddSingleton<PlayerSaidHandler>();

                // Commands — registered as ICommand so CommandDispatcher discovers them all
                services.AddSingleton<ICommand, LookCommand>();
                services.AddSingleton<ICommand, SayCommand>();
                foreach (var dir in Enum.GetValues<Direction>())
                {
                    var captured = dir;
                    services.AddSingleton<ICommand>(sp => new MoveCommand(
                        captured,
                        sp.GetRequiredService<IMovementSystem>(),
                        sp.GetRequiredService<IEventBus>()));
                }

                // Persistence substrate (Phase 3 slice 1)
                services.AddPersistenceModule();

                // Hosted services — order matters: PersistenceBootstrap must complete StartAsync
                // before TelnetServer begins accepting connections.
                services.AddHostedService<PersistenceBootstrap>();
                services.AddHostedService<PersistenceFlushTimer>();
                services.AddHostedService<TelnetServer>();
            })
            .Build();

        // Subscribe handlers to the event bus before accepting connections
        var bus = host.Services.GetRequiredService<IEventBus>();
        bus.Subscribe<PlayerConnectedEvent>(host.Services.GetRequiredService<PlayerSessionHandler>());
        bus.Subscribe<PlayerDisconnectedEvent>(host.Services.GetRequiredService<PlayerSessionHandler>());
        bus.Subscribe<PlayerMovedEvent>(host.Services.GetRequiredService<PlayerMovedHandler>());
        bus.Subscribe<PlayerSaidEvent>(host.Services.GetRequiredService<PlayerSaidHandler>());

        // World bootstrap — build the three-room world before accepting connections
        WorldBootstrap.Initialize(
            host.Services.GetRequiredService<EntityService>(),
            host.Services.GetRequiredService<WorldConfiguration>());

        await host.RunAsync();
    }
}
