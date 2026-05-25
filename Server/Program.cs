using Hedron.Core;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Commands.Events;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Handlers;
using Hedron.Core.Modules.Account;
using Hedron.Core.Modules.Account.Handlers;
using Hedron.Core.Modules.Admin;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Handlers;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Chat.Commands;
using Hedron.Core.Modules.Chat.Events;
using Hedron.Core.Modules.Chat.Handlers;
using Hedron.Core.Modules.Help;
using Hedron.Core.Modules.Movement.Commands;
using Hedron.Core.Modules.Movement.Events;
using Hedron.Core.Modules.Movement.Handlers;
using Hedron.Core.Modules.Movement.Systems;
using Hedron.Core.Modules.Persistence;
using Hedron.Core.Modules.Session.Events;
using Hedron.Core.Modules.Session.Handlers;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Output;
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
            .ConfigureServices((context, services) =>
            {
                services.Configure<OutputConfiguration>(
                    context.Configuration.GetSection("Output"));
                // ECS world
                var world = new EntityService();
                EcsManager.SetWorld(world);
                services.AddSingleton(world);

                services.AddSingleton<IEventBus, EventBus>();
                services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
                // CommandDispatcher implements IVerbRegistry; expose the same singleton under both interfaces.
                services.AddSingleton<IVerbRegistry>(sp =>
                    (IVerbRegistry)sp.GetRequiredService<ICommandDispatcher>());
                services.AddSingleton<ISessionManager, SessionManager>();

                // Command framework — argument parser and output writer factory
                services.AddSingleton<ICommandArgumentParser, CommandArgumentParser>();
                // Output formatters — register concrete formatters before the registry.
                services.AddSingleton<IOutputFormatter, TelnetOutputFormatter>();
                services.AddSingleton<IOutputFormatterRegistry, OutputFormatterRegistry>();
                services.AddSingleton<IOutputWriterFactory, OutputWriterFactory>();

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
                services.AddSingleton<CommandLoggingHandler>();

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

                // Modules — Account, World, Admin (registers IAuthorizationChecker), Help, Items
                services.AddAccountModule();
                services.AddPersistenceModule();
                services.AddWorldModule();
                services.AddAdminModule();
                services.AddHelpModule();
                services.AddItemsModule();

                // Hosted services — order matters.
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
        bus.Subscribe<RoomCreatedByAdminEvent>(audit);
        bus.Subscribe<RoomPropertySetByAdminEvent>(audit);
        bus.Subscribe<ContentReloadedEvent>(audit);
        bus.Subscribe<ItemCreatedByAdminEvent>(audit);
        bus.Subscribe<ItemPropertySetByAdminEvent>(audit);

        var characterHydration = host.Services.GetRequiredService<CharacterHydrationHandler>();
        bus.Subscribe<WorldContentReadyEvent>(characterHydration);

        var commandLogging = host.Services.GetRequiredService<CommandLoggingHandler>();
        bus.Subscribe<CommandExecutedEvent>(commandLogging);

        await host.RunAsync();
    }
}
