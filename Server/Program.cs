using Hedron.Core;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Handlers;
using Hedron.Core.Modules.Admin;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Handlers;
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
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;
using Hedron.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hedron.Server;

/// <summary>
/// Composition root.
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

                // World configuration — populated by WorldContentLoader before the listener starts.
                var worldConfig = new WorldConfiguration();
                services.AddSingleton(worldConfig);

                // Handlers
                services.AddSingleton<PlayerSessionHandler>();
                services.AddSingleton<PlayerMovedHandler>();
                services.AddSingleton<PlayerSaidHandler>();

                // Player-facing commands
                services.AddSingleton<ICommand, SayCommand>();
                foreach (var dir in Enum.GetValues<Direction>())
                {
                    var captured = dir;
                    services.AddSingleton<ICommand>(sp => new MoveCommand(
                        captured,
                        sp.GetRequiredService<IMovementSystem>(),
                        sp.GetRequiredService<IEventBus>()));
                }

                // Modules — World registers TemplateRegistry, ContentSerializer, ContentLoader,
                // and the LookCommand. Admin registers the authorizer, four admin commands,
                // and the audit handler.
                services.AddPersistenceModule();
                services.AddWorldModule();
                services.AddAdminModule();

                // Hosted services — order matters. PersistenceBootstrap loads persisted entities
                // and publishes WorldLoadedEvent; WorldContentBootstrap then registers authored
                // templates and seeds any missing entities; TelnetServer accepts connections last.
                services.AddHostedService<PersistenceBootstrap>();
                services.AddHostedService<WorldContentBootstrap>();
                services.AddHostedService<PersistenceFlushTimer>();
                services.AddHostedService<TelnetServer>();
            })
            .Build();

        // Subscribe handlers to the event bus before accepting connections.
        var bus = host.Services.GetRequiredService<IEventBus>();
        bus.Subscribe<PlayerConnectedEvent>(host.Services.GetRequiredService<PlayerSessionHandler>());
        bus.Subscribe<PlayerDisconnectedEvent>(host.Services.GetRequiredService<PlayerSessionHandler>());
        bus.Subscribe<PlayerMovedEvent>(host.Services.GetRequiredService<PlayerMovedHandler>());
        bus.Subscribe<PlayerTeleportedByAdminEvent>(host.Services.GetRequiredService<PlayerMovedHandler>());
        bus.Subscribe<PlayerSaidEvent>(host.Services.GetRequiredService<PlayerSaidHandler>());

        var audit = host.Services.GetRequiredService<AdminAuditHandler>();
        bus.Subscribe<EntitySpawnedByAdminEvent>(audit);
        bus.Subscribe<PlayerTeleportedByAdminEvent>(audit);
        bus.Subscribe<RoomExitAuthoredByAdminEvent>(audit);
        bus.Subscribe<ContentReloadedEvent>(audit);

        var persistenceHandler = host.Services.GetRequiredService<PersistenceHandler>();
        bus.Subscribe<EntitySpawnedByAdminEvent>(persistenceHandler);
        bus.Subscribe<RoomExitAuthoredByAdminEvent>(persistenceHandler);

        await host.RunAsync();
    }
}
