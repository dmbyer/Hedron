using Hedron.Core;
using Hedron.Core.Commands;
using Microsoft.Extensions.Configuration;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Commands.Events;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Handlers;
using Hedron.Core.Modules.Account;
using Hedron.Core.Modules.Account.Handlers;
using Hedron.Core.Modules.Admin;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Handlers;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Items.Handlers;
using Hedron.Core.Modules.Attributes;
using Hedron.Core.Modules.Attributes.Events;
using Hedron.Core.Modules.EntityState;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Chat.Commands;
using Hedron.Core.Modules.Chat.Events;
using Hedron.Core.Modules.Chat.Handlers;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Death.Commands;
using Hedron.Core.Modules.Death.Events;
using Hedron.Core.Modules.Death.Handlers;
using Hedron.Core.Modules.Help;
using Hedron.Core.Modules.Movement.Commands;
using Hedron.Core.Modules.Movement.Events;
using Hedron.Core.Modules.Movement.Handlers;
using Hedron.Core.Modules.Movement.Systems;
using Hedron.Core.Modules.Persistence;
using Hedron.Core.Modules.Session.Events;
using Hedron.Core.Modules.Session.Handlers;
using Hedron.Core.Modules.Combat;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Combat.Handlers;
using Hedron.Core.Modules.Spawn;
using Hedron.Core.Modules.Spawn.Handlers;
using Hedron.Core.Modules.Spawn.Systems;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Abilities.Events;
using Hedron.Core.Modules.Abilities.Handlers;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Regeneration;
using Hedron.Core.Modules.Regeneration.Handlers;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Events;
using Hedron.Core.Modules.Effects.Handlers;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Shopping;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Time;
using Hedron.Core.Modules.Time.Events;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Output;
using Hedron.Core.Modules.Prompt.Systems;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;
using Hedron.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Server;

public static class CompositionRoot
{
    public static IServiceCollection Register(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OutputConfiguration>(
            configuration.GetSection("Output"));
        services.Configure<DeathOptions>(
            configuration.GetSection("Death"));
        services.Configure<WorldOptions>(
            configuration.GetSection("World"));
        services.Configure<PersistenceOptions>(
            configuration.GetSection("Persistence"));
        services.Configure<CharacterDefaultsOptions>(
            configuration.GetSection("CharacterDefaults"));
        services.Configure<ShopOptions>(
            configuration.GetSection("Shop"));
        // ECS world
        var world = new EntityService();
        EcsManager.SetWorld(world);
        services.AddSingleton(world);
        services.AddSingleton<IArchetypeRegistry, ArchetypeRegistry>();

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
        services.AddSingleton<ISessionBufferRegistry, SessionBufferRegistry>();
        services.AddSingleton<IPromptSource, PromptComposerSystem>();

        // Systems
        services.AddSingleton<IRandom, SystemRandom>();
        services.AddSingleton<IClock, SystemClock>();
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
        services.AddSingleton<OutputFlushTickHandler>();

        // Player-facing commands
        services.AddSingleton<ICommand, SayCommand>();
        foreach (var dir in Enum.GetValues<Direction>())
        {
            var captured = dir;
            services.AddSingleton<ICommand>(sp => new MoveCommand(
                captured,
                sp.GetRequiredService<IMovementSystem>(),
                sp.GetRequiredService<IEntityStateService>(),
                sp.GetRequiredService<IEventBus>()));
        }

        // Modules — Account, World, Admin (registers IAuthorizationChecker), Help, Items
        services.AddAccountModule();
        services.AddPersistenceModule();
        services.AddWorldModule();
        services.AddAuthoringModule();
        services.AddAdminModule();
        services.AddHelpModule();
        services.AddItemsModule();
        services.AddMobsModule();
        services.AddAttributesModule();
        services.AddEntityStateModule();
        services.AddTimeModule();
        services.AddStatsModule();
        services.AddAspectsModule();
        services.AddEffectsModule();
        services.AddAbilitiesModule();
        services.AddCombatModule();
        services.AddSpawnModule();
        services.AddDeathModule();
        services.AddRegenerationModule();
        services.AddEconomyModule();
        services.AddShoppingModule();

        return services;
    }

    /// <summary>
    /// Composes the hosted services for the full gameplay host (the telnet server): startup
    /// bootstraps, the periodic persistence flush, the telnet listener, and the heartbeat.
    /// Kept separate from <see cref="Register"/> (which is pure DI) so that other hosts — e.g.
    /// the content-authoring web host — compose their own hosted-service set without inheriting
    /// the heartbeat/listener. Order matters: bootstraps run before the listener and heartbeat.
    /// </summary>
    public static IServiceCollection AddGameplayHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<PersistenceBootstrap>();
        services.AddHostedService<WorldContentBootstrap>();
        services.AddHostedService<RegistryValidationBootstrap>();
        services.AddHostedService<PersistenceFlushTimer>();
        services.AddHostedService<TelnetServer>();
        services.AddHostedService<HeartbeatBackgroundService>();

        return services;
    }

    /// <summary>
    /// Composes the hosted services for the content-authoring web host (<c>Hedron.Web</c>): only
    /// the two startup bootstraps that give the editor and the on-demand validator data to work
    /// against — <see cref="WorldContentBootstrap"/> (load + register the authored YAML so the
    /// catalog/preview and registry validation have content) and <see cref="RegistryValidationBootstrap"/>
    /// (fail-fast on bad content at boot).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately registers <b>neither</b> <c>PersistenceBootstrap</c>/<c>PersistenceFlushTimer</c>
    /// (no SQLite — authoring writes YAML only), <c>TelnetServer</c> (no players), nor
    /// <c>HeartbeatBackgroundService</c> (authoring is off the tick). The web host re-derives a
    /// preview/validation world from YAML; it never marshals world-mutating work onto a game loop.
    /// </para>
    /// <para>
    /// This is the sibling of <see cref="AddGameplayHostedServices"/>: <see cref="Register"/> stays
    /// pure DI and each host composes its own hosted-service set, so the same process can later scale
    /// from authoring-only to the full engine + web superset without reshaping the shared registration.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddContentBootstrapHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<WorldContentBootstrap>();
        services.AddHostedService<RegistryValidationBootstrap>();

        return services;
    }
}
