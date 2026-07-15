using Hedron.Core.Commands.Events;
using Hedron.Core.Modules.Account.Handlers;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Handlers;
using Hedron.Core.Modules.Attributes.Events;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Items.Handlers;
using Hedron.Core.Modules.Chat.Events;
using Hedron.Core.Modules.Chat.Handlers;
using Hedron.Core.Modules.Death.Events;
using Hedron.Core.Modules.Death.Handlers;
using Hedron.Core.Modules.Movement.Events;
using Hedron.Core.Modules.Movement.Handlers;
using Hedron.Core.Modules.Session.Events;
using Hedron.Core.Modules.Session.Handlers;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Combat.Handlers;
using Hedron.Core.Modules.Spawn.Systems;
using Hedron.Core.Modules.Spawn.Handlers;
using Hedron.Core.Modules.Abilities.Events;
using Hedron.Core.Modules.Abilities.Handlers;
using Hedron.Core.Modules.Regeneration.Handlers;
using Hedron.Core.Modules.Effects.Events;
using Hedron.Core.Modules.Effects.Handlers;
using Hedron.Core.Modules.Time.Events;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Events;
using Hedron.Core.Handlers;
using Hedron.Core.Modules.Economy.Events;
using Hedron.Core.Modules.Economy.Handlers;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Ascension.Events;
using Hedron.Core.Modules.Ascension.Handlers;
using Hedron.Core.Modules.Progression.Handlers;
using Hedron.Core.Modules.Shopping.Events;
using Hedron.Core.Modules.Shopping.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hedron.Server;

/// <summary>
/// Composition root.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Headless one-shot run-modes branch before the listener host is built (INV-10 no-chain
        // Initiator): they compose DI, run one operation, and exit — no telnet/heartbeat.
        if (GenerationRunMode.Matches(args))
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables("HEDRON_")
                .Build();
            return await GenerationRunMode.RunAsync(args, configuration);
        }

        if (SimulateRunMode.Matches(args))
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables("HEDRON_")
                .Build();
            return await SimulateRunMode.RunAsync(args, configuration);
        }

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, cfg) => cfg.AddEnvironmentVariables("HEDRON_"))
            .ConfigureServices((context, services) =>
            {
                services.Register(context.Configuration);
                services.AddGameplayHostedServices();
            })
            .Build();

        // Subscribe handlers to the event bus before accepting connections.
        var bus = host.Services.GetRequiredService<IEventBus>();
        bus.Subscribe<PlayerConnectedEvent>(host.Services.GetRequiredService<PlayerSessionHandler>());
        bus.Subscribe<PlayerDisconnectedEvent>(host.Services.GetRequiredService<PlayerSessionHandler>());
        bus.Subscribe<PlayerMovedEvent>(host.Services.GetRequiredService<PlayerMovedHandler>());
        bus.Subscribe<PlayerTeleportedByAdminEvent>(host.Services.GetRequiredService<PlayerMovedHandler>());
        bus.Subscribe<PlayerSaidEvent>(host.Services.GetRequiredService<PlayerSaidHandler>());

        var effectTick = host.Services.GetRequiredService<EffectTickHandler>();
        bus.Subscribe<HeartbeatTickEvent>(effectTick);

        var abilityCooldownTick = host.Services.GetRequiredService<AbilityCooldownTickHandler>();
        bus.Subscribe<HeartbeatTickEvent>(abilityCooldownTick);

        var combatTick = host.Services.GetRequiredService<CombatTickHandler>();
        bus.Subscribe<HeartbeatTickEvent>(combatTick);

        var regenTick = host.Services.GetRequiredService<RegenerationTickHandler>();
        bus.Subscribe<HeartbeatTickEvent>(regenTick);

        var outputFlushTick = host.Services.GetRequiredService<OutputFlushTickHandler>();
        bus.Subscribe<HeartbeatTickEvent>(outputFlushTick);

        var combatOutput = host.Services.GetRequiredService<CombatHandler>();
        bus.Subscribe<CombatStartedEvent>(combatOutput);
        bus.Subscribe<CombatRoundEvent>(combatOutput);
        bus.Subscribe<CombatEndedEvent>(combatOutput);

        var combatMobDeath = host.Services.GetRequiredService<CombatMobDeathHandler>();
        bus.Subscribe<CombatEndedEvent>(combatMobDeath);

        var abilityStrike = host.Services.GetRequiredService<AbilityStrikeHandler>();
        bus.Subscribe<AbilityStrikeResolvedEvent>(abilityStrike);

        var abilityInvocation = host.Services.GetRequiredService<AbilityInvocationHandler>();
        bus.Subscribe<AbilityActivatedEvent>(abilityInvocation);

        var deathTick = host.Services.GetRequiredService<DeathTickHandler>();
        bus.Subscribe<HeartbeatTickEvent>(deathTick);

        var playerDeath = host.Services.GetRequiredService<PlayerDeathHandler>();
        bus.Subscribe<PlayerDiedEvent>(playerDeath);

        var deathNarration = host.Services.GetRequiredService<DeathNarrationHandler>();
        bus.Subscribe<PlayerIncapacitatedEvent>(deathNarration);
        bus.Subscribe<PlayerBleedingEvent>(deathNarration);
        bus.Subscribe<PlayerDiedEvent>(deathNarration);
        bus.Subscribe<PlayerRespawnedEvent>(deathNarration);

        var audit = host.Services.GetRequiredService<AdminAuditHandler>();
        bus.Subscribe<EntitySpawnedByAdminEvent>(audit);
        bus.Subscribe<PlayerTeleportedByAdminEvent>(audit);
        bus.Subscribe<RoomExitAuthoredByAdminEvent>(audit);
        bus.Subscribe<RoomCreatedByAdminEvent>(audit);
        bus.Subscribe<RoomPropertySetByAdminEvent>(audit);
        bus.Subscribe<ContentReloadedEvent>(audit);
        bus.Subscribe<ItemCreatedByAdminEvent>(audit);
        bus.Subscribe<ItemPropertySetByAdminEvent>(audit);
        bus.Subscribe<MobCreatedByAdminEvent>(audit);
        bus.Subscribe<MobPropertySetByAdminEvent>(audit);
        bus.Subscribe<PlayerAttributeSetByAdminEvent>(audit);
        bus.Subscribe<CombatEndedEvent>(audit);
        bus.Subscribe<EffectAppliedByAdminEvent>(audit);
        bus.Subscribe<PlayerRespawnSetByAdminEvent>(audit);
        bus.Subscribe<AbilityTaughtByAdminEvent>(audit);
        bus.Subscribe<RoomAreaAssignedByAdminEvent>(audit);
        bus.Subscribe<AreaCreatedByAdminEvent>(audit);
        bus.Subscribe<WalletSetByAdminEvent>(audit);
        bus.Subscribe<PlayerAscendedByAdminEvent>(audit);

        var characterHydration = host.Services.GetRequiredService<CharacterHydrationHandler>();
        bus.Subscribe<WorldContentReadyEvent>(characterHydration);

        var shopkeeperSpawn = host.Services.GetRequiredService<ShopkeeperSpawnHandler>();
        bus.Subscribe<WorldContentReadyEvent>(shopkeeperSpawn);

        var itemInteraction = host.Services.GetRequiredService<ItemInteractionHandler>();
        bus.Subscribe<ItemPickedUpEvent>(itemInteraction);
        bus.Subscribe<ItemDroppedEvent>(itemInteraction);

        var equipmentInteraction = host.Services.GetRequiredService<EquipmentInteractionHandler>();
        bus.Subscribe<ItemEquippedEvent>(equipmentInteraction);
        bus.Subscribe<ItemUnequippedEvent>(equipmentInteraction);

        var commandLogging = host.Services.GetRequiredService<CommandLoggingHandler>();
        bus.Subscribe<CommandExecutedEvent>(commandLogging);

        var spawnSystem = host.Services.GetRequiredService<SpawnSystem>();
        bus.Subscribe<WorldContentReadyEvent>(spawnSystem);
        bus.Subscribe<MobDiedEvent>(spawnSystem);
        bus.Subscribe<ItemPickedUpEvent>(spawnSystem);
        bus.Subscribe<HeartbeatTickEvent>(spawnSystem);

        var itemContext = host.Services.GetRequiredService<ItemContextHandler>();
        bus.Subscribe<ItemPickedUpEvent>(itemContext);
        bus.Subscribe<ItemDroppedEvent>(itemContext);
        bus.Subscribe<ItemBoughtEvent>(itemContext);
        bus.Subscribe<ItemSoldEvent>(itemContext);

        var shopInteraction = host.Services.GetRequiredService<ShopInteractionHandler>();
        bus.Subscribe<ItemBoughtEvent>(shopInteraction);
        bus.Subscribe<ItemSoldEvent>(shopInteraction);

        var currencyLoot = host.Services.GetRequiredService<CurrencyLootHandler>();
        bus.Subscribe<MobDiedEvent>(currencyLoot);

        var experienceAward = host.Services.GetRequiredService<ExperienceAwardHandler>();
        bus.Subscribe<MobDiedEvent>(experienceAward);

        var ascensionNarration = host.Services.GetRequiredService<AscensionNarrationHandler>();
        bus.Subscribe<AscendedEvent>(ascensionNarration);

        var currencyAwardNarration = host.Services.GetRequiredService<CurrencyAwardNarrationHandler>();
        bus.Subscribe<CurrencyAwardedEvent>(currencyAwardNarration);

        var shopRestock = host.Services.GetRequiredService<ShopRestockTickHandler>();
        bus.Subscribe<HeartbeatTickEvent>(shopRestock);

        var shopExpiry = host.Services.GetRequiredService<ShopExpiryTickHandler>();
        bus.Subscribe<HeartbeatTickEvent>(shopExpiry);

        await host.RunAsync();
        return 0;
    }
}

