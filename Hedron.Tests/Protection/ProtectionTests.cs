using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Aspects.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Combat.Commands;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Commands;
using Hedron.Core.Modules.Effects.Events;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Protection
{
    /// <summary>
    /// WP-1 tests — Protection component + dual gate (combat + effects).
    ///
    /// Tier 1 (system-unit): <see cref="CombatSystem.CanBeAttacked"/> decisions;
    ///   <see cref="EffectSystem.Apply"/> immune path (harmful + beneficial + regression).
    /// Tier 3 (flow / command): Gate A refusal through <see cref="KillCommand"/>;
    ///   Gate B refusal through <see cref="AffectCommand"/>.
    /// Tier 5 (architecture guard): <see cref="ProtectionComponent"/> is NOT <c>[Persistent]</c>.
    ///
    /// Coverage contract: Postconditions and Test plan from docs/implementation-plans/mob-protection.md WP-1.
    /// </summary>
    public sealed class ProtectionTests
    {
        // ════════════════════════════════════════════════════════════════════════
        // ── Tier 1 — CombatSystem.CanBeAttacked ─────────────────────────────
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// CanBeAttacked returns false for an entity whose ProtectionComponent has
        /// the Untargetable flag set.
        /// </summary>
        [Fact]
        public void CanBeAttacked_returns_false_for_Untargetable_entity()
        {
            var ecs = new EntityService();
            var combat = BuildCombatSystem(ecs, new FakeRandom(1));

            var mobId = new EntityBuilder(ecs).AsMob("shopkeeper").Build();
            ecs.AddComponent(mobId, new ProtectionComponent { Flags = ProtectionFlags.Untargetable });

            Assert.False(combat.CanBeAttacked(mobId),
                "Untargetable mob must not be attackable");
        }

        /// <summary>
        /// CanBeAttacked returns true for an entity that has EffectImmune only
        /// (Untargetable not set).
        /// </summary>
        [Fact]
        public void CanBeAttacked_returns_true_for_EffectImmune_only()
        {
            var ecs = new EntityService();
            var combat = BuildCombatSystem(ecs, new FakeRandom(1));

            var mobId = new EntityBuilder(ecs).AsMob("crowd-control-immune boss").Build();
            ecs.AddComponent(mobId, new ProtectionComponent { Flags = ProtectionFlags.EffectImmune });

            Assert.True(combat.CanBeAttacked(mobId),
                "EffectImmune-only mob must still be attackable (different axis)");
        }

        /// <summary>
        /// CanBeAttacked returns true for an entity with no ProtectionComponent.
        /// </summary>
        [Fact]
        public void CanBeAttacked_returns_true_when_no_ProtectionComponent()
        {
            var ecs = new EntityService();
            var combat = BuildCombatSystem(ecs, new FakeRandom(1));

            var mobId = new EntityBuilder(ecs).AsMob("rat").Build();

            Assert.True(combat.CanBeAttacked(mobId),
                "Unprotected mob must be attackable");
        }

        /// <summary>
        /// CanBeAttacked returns true for ProtectionFlags.None (component present but empty).
        /// </summary>
        [Fact]
        public void CanBeAttacked_returns_true_for_ProtectionFlags_None()
        {
            var ecs = new EntityService();
            var combat = BuildCombatSystem(ecs, new FakeRandom(1));

            var mobId = new EntityBuilder(ecs).AsMob("goblin").Build();
            ecs.AddComponent(mobId, new ProtectionComponent { Flags = ProtectionFlags.None });

            Assert.True(combat.CanBeAttacked(mobId),
                "ProtectionFlags.None must still be attackable");
        }

        // ════════════════════════════════════════════════════════════════════════
        // ── Tier 1 — EffectSystem.Apply immune path ──────────────────────────
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply returns Immune (EffectApplyResult.NotApplied with Reason=Immune) for a
        /// harmful effect definition when the target has EffectImmune.
        /// No EffectsComponent is created or mutated.
        /// </summary>
        [Fact]
        public void Apply_returns_Immune_for_harmful_effect_on_EffectImmune_target()
        {
            var ecs = new EntityService();
            var system = new EffectSystem(ecs, Array.Empty<IEffectContributor>());

            var targetId = new EntityBuilder(ecs).AsMob("shopkeeper").Build();
            ecs.AddComponent(targetId, new ProtectionComponent { Flags = ProtectionFlags.EffectImmune });

            var sourceId = new EntityBuilder(ecs).AsPlayer().Build();

            // Harmful definition: negative BaseMagnitude (damage)
            var harmfulDef = new EffectDefinition(
                EffectId: "poison",
                Kind: EffectKind.StatModifier,
                Params: new EffectParams(TargetScore: ScoreId.Body, BaseMagnitude: -5),
                Category: EffectCategory.Poison,
                PowerScalingFormula: "fixed",
                Duration: -1f,
                Stacking: StackPolicy.Stack,
                Phase: EffectPhase.Normal);

            var result = system.Apply(targetId, harmfulDef, sourceId);

            var notApplied = Assert.IsType<EffectApplyResult.NotApplied>(result);
            Assert.Equal(EffectNotAppliedReason.Immune, notApplied.Reason);
            Assert.False(ecs.HasComponent<EffectsComponent>(targetId),
                "EffectsComponent must NOT be created for an immune target");
        }

        /// <summary>
        /// Apply returns Immune for a BENEFICIAL effect definition when the target has
        /// EffectImmune — protection means "nothing lands," not just "no harmful effects."
        /// Design decision 2 (mob-protection.md Resolved decisions).
        /// </summary>
        [Fact]
        public void Apply_returns_Immune_for_beneficial_effect_on_EffectImmune_target()
        {
            var ecs = new EntityService();
            var system = new EffectSystem(ecs, Array.Empty<IEffectContributor>());

            var targetId = new EntityBuilder(ecs).AsMob("shopkeeper").Build();
            ecs.AddComponent(targetId, new ProtectionComponent { Flags = ProtectionFlags.EffectImmune });

            var sourceId = new EntityBuilder(ecs).AsPlayer().Build();

            // Beneficial definition: positive BaseMagnitude (heal / empower)
            var buffDef = new EffectDefinition(
                EffectId: "empower",
                Kind: EffectKind.StatModifier,
                Params: new EffectParams(TargetScore: ScoreId.Body, BaseMagnitude: 10),
                Category: EffectCategory.Buff,
                PowerScalingFormula: "fixed",
                Duration: 30f,
                Stacking: StackPolicy.HighestWins,
                Phase: EffectPhase.Normal);

            var result = system.Apply(targetId, buffDef, sourceId);

            var notApplied = Assert.IsType<EffectApplyResult.NotApplied>(result);
            Assert.Equal(EffectNotAppliedReason.Immune, notApplied.Reason);
            Assert.False(ecs.HasComponent<EffectsComponent>(targetId),
                "EffectsComponent must NOT be created for an immune target (beneficial effects also blocked)");
        }

        /// <summary>
        /// Regression: Apply behaves exactly as before when the target has NO ProtectionComponent.
        /// A buffed entity receives the effect normally (non-immune path unaffected).
        /// </summary>
        [Fact]
        public void Apply_unprotected_target_receives_effect_normally()
        {
            var ecs = new EntityService();
            var system = new EffectSystem(ecs, Array.Empty<IEffectContributor>());

            var targetId = new EntityBuilder(ecs).AsMob("goblin").Build();
            var sourceId = new EntityBuilder(ecs).AsPlayer().Build();

            var def = new EffectDefinition(
                EffectId: "empower",
                Kind: EffectKind.StatModifier,
                Params: new EffectParams(TargetScore: ScoreId.Body, BaseMagnitude: 5),
                Category: EffectCategory.Buff,
                PowerScalingFormula: "fixed",
                Duration: 30f,
                Stacking: StackPolicy.Stack,
                Phase: EffectPhase.Normal);

            var result = system.Apply(targetId, def, sourceId);

            var applied = Assert.IsType<EffectApplyResult.Applied>(result);
            Assert.Equal("empower", applied.Effect.EffectId);
            Assert.True(ecs.HasComponent<EffectsComponent>(targetId),
                "EffectsComponent must be created for an unprotected target");
        }

        /// <summary>
        /// Regression: EffectImmune flag alone does not affect an unprotected entity.
        /// If the entity has the component but Untargetable only (not EffectImmune),
        /// Apply proceeds normally.
        /// </summary>
        [Fact]
        public void Apply_Untargetable_only_does_not_block_effects()
        {
            var ecs = new EntityService();
            var system = new EffectSystem(ecs, Array.Empty<IEffectContributor>());

            var targetId = new EntityBuilder(ecs).AsMob("ghost").Build();
            // Untargetable but NOT EffectImmune
            ecs.AddComponent(targetId, new ProtectionComponent { Flags = ProtectionFlags.Untargetable });
            var sourceId = new EntityBuilder(ecs).AsPlayer().Build();

            var def = new EffectDefinition(
                EffectId: "poison",
                Kind: EffectKind.StatModifier,
                Params: new EffectParams(TargetScore: ScoreId.Body, BaseMagnitude: -3),
                Category: EffectCategory.Poison,
                PowerScalingFormula: "fixed",
                Duration: 30f,
                Stacking: StackPolicy.Stack,
                Phase: EffectPhase.Normal);

            var result = system.Apply(targetId, def, sourceId);

            // Must succeed — only Untargetable is set, not EffectImmune
            Assert.IsType<EffectApplyResult.Applied>(result);
        }

        // ════════════════════════════════════════════════════════════════════════
        // ── Tier 3 — Gate A: KillCommand refusal for Untargetable mob ────────
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gate A flow test: KillCommand against an Untargetable mob must:
        ///   - Write a refusal message to the player.
        ///   - NOT attach CombatStateComponent to either entity.
        ///   - NOT publish CombatStartedEvent.
        /// </summary>
        [Fact]
        public async Task KillCommand_refuses_attack_on_Untargetable_mob()
        {
            // Arrange
            var ecs = new EntityService();
            var rng = new FakeRandom(1);
            var combatSystem = BuildCombatSystem(ecs, rng);
            var entityStateService = new EntityStateService(ecs);
            var bus = new RecordingEventBus(dispatch: false);

            const uint roomId = 1u;

            var playerId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .InRoom(roomId)
                .Build();

            var mobId = new EntityBuilder(ecs)
                .AsMob("shopkeeper")
                .InRoom(roomId)
                .Build();

            ecs.AddComponent(mobId, new ProtectionComponent { Flags = ProtectionFlags.Untargetable });

            var cmd = new KillCommand(combatSystem, entityStateService, ecs, bus,
                NullLogger<KillCommand>.Instance);

            var output = new RecordingOutput();
            var writer = output.WriterFor(playerId);
            var args = MakeArgs(new Dictionary<string, object?> { ["target"] = "shopkeeper" });
            var context = new CommandContext(
                new StubSession(playerId), playerId, args, writer,
                Services: null!);

            // Act
            await cmd.ExecuteAsync(context);

            // Assert — refusal message written
            Assert.True(output.All.Count > 0,
                "A refusal message must be written to the player");

            // Assert — no combat state transition
            Assert.False(ecs.HasComponent<CombatStateComponent>(playerId),
                "Player must NOT have CombatStateComponent after refusal");
            Assert.False(ecs.HasComponent<CombatStateComponent>(mobId),
                "Mob must NOT have CombatStateComponent after refusal");

            // Assert — no CombatStartedEvent published
            var combatStartedEvents = bus.Published.OfType<CombatStartedEvent>().ToList();
            Assert.Empty(combatStartedEvents);
        }

        /// <summary>
        /// Regression: KillCommand against an unprotected mob still begins combat normally.
        /// </summary>
        [Fact]
        public async Task KillCommand_starts_combat_on_unprotected_mob()
        {
            var ecs = new EntityService();
            var rng = new FakeRandom(1);
            var combatSystem = BuildCombatSystem(ecs, rng);
            var entityStateService = new EntityStateService(ecs);
            var bus = new RecordingEventBus(dispatch: false);

            const uint roomId = 2u;

            var playerId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .InRoom(roomId)
                .Build();

            var mobId = new EntityBuilder(ecs)
                .AsMob("goblin")
                .InRoom(roomId)
                .Build();

            var cmd = new KillCommand(combatSystem, entityStateService, ecs, bus,
                NullLogger<KillCommand>.Instance);

            var output = new RecordingOutput();
            var args = MakeArgs(new Dictionary<string, object?> { ["target"] = "goblin" });
            var context = new CommandContext(
                new StubSession(playerId), playerId, args, output.WriterFor(playerId),
                Services: null!);

            await cmd.ExecuteAsync(context);

            // Combat must have started (CombatStartedEvent published)
            var combatStartedEvents = bus.Published.OfType<CombatStartedEvent>().ToList();
            Assert.NotEmpty(combatStartedEvents);
            Assert.True(ecs.HasComponent<CombatStateComponent>(playerId),
                "Player must be in combat after attacking unprotected mob");
        }

        // ════════════════════════════════════════════════════════════════════════
        // ── Tier 3 — Gate B: AffectCommand refusal for EffectImmune mob ──────
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gate B flow test: AffectCommand against an EffectImmune mob must:
        ///   - Write an immune message to the admin.
        ///   - NOT publish EffectAppliedEvent.
        /// </summary>
        [Fact]
        public async Task AffectCommand_does_not_publish_EffectAppliedEvent_for_EffectImmune_target()
        {
            var ecs = new EntityService();
            var effectSystem = new EffectSystem(ecs, Array.Empty<IEffectContributor>());
            var effectRegistry = BuildEffectRegistry();
            var bus = new RecordingEventBus(dispatch: false);

            var noEffects = new EffectSystem(ecs, Array.Empty<IEffectContributor>());
            var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
            var attributeSystem = new AttributeSystem(ecs, noEffects, deathOpts);

            var adminId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithPools(hp: 100)
                .Build();

            var mobId = new EntityBuilder(ecs)
                .AsMob("shopkeeper")
                .Build();

            ecs.AddComponent(mobId, new ProtectionComponent { Flags = ProtectionFlags.EffectImmune });

            // Use a fake session manager that provides the admin session
            var sessionManager = new SingleSessionManager(new StubSession(adminId));

            var cmd = new AffectCommand(
                effectSystem, effectRegistry, attributeSystem, ecs, bus, sessionManager);

            var output = new RecordingOutput();
            var args = MakeArgs(new Dictionary<string, object?>
            {
                ["target"] = "shopkeeper",
                ["effectId"] = "empower",
                ["power"] = null,
            });
            var context = new CommandContext(
                new StubSession(adminId), adminId, args, output.WriterFor(adminId),
                Services: null!);

            await cmd.ExecuteAsync(context);

            // No EffectAppliedEvent published for an immune target
            var effectAppliedEvents = bus.Published.OfType<EffectAppliedEvent>().ToList();
            Assert.Empty(effectAppliedEvents);

            // An output message was written (immune feedback)
            Assert.True(output.All.Count > 0,
                "An immune-feedback message must be written to the admin");
        }

        // ════════════════════════════════════════════════════════════════════════
        // ── Tier 5 — Architecture guard: ProtectionComponent not [Persistent] ─
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// INV-23: <see cref="ProtectionComponent"/> must NOT carry <c>[Persistent]</c>.
        /// It is world-content (authored on mobs via YAML); its durable form is the mob template,
        /// not the SQLite snapshot.
        /// </summary>
        [Fact]
        public void ProtectionComponent_is_not_Persistent()
        {
            var hasAttribute = typeof(ProtectionComponent)
                .GetCustomAttributes(typeof(PersistentAttribute), inherit: false)
                .Length > 0;

            Assert.False(hasAttribute,
                "INV-23: ProtectionComponent must NOT carry [Persistent] — " +
                "it is world-content on mobs, re-spawned from YAML. " +
                "Its durable form is MobTemplate.Protection, not a SQLite row.");
        }

        // ════════════════════════════════════════════════════════════════════════
        // ── Helpers ─────────────────────────────────────────────════════════────
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a <see cref="ParsedArguments"/> via the internal constructor (reflection).
        /// Pattern established by SetitemValueCommandTests and SetwalletCommandTests.
        /// </summary>
        private static ParsedArguments MakeArgs(Dictionary<string, object?> values)
        {
            var ctor = typeof(ParsedArguments).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(IReadOnlyDictionary<string, object?>) },
                null)!;
            return (ParsedArguments)ctor.Invoke(new object[] { values });
        }

        private static CombatSystem BuildCombatSystem(EntityService ecs, FakeRandom rng)
        {
            var noEffects = new EffectSystem(ecs, Array.Empty<IEffectContributor>());
            var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
            var attributes = new AttributeSystem(ecs, noEffects, deathOpts);
            var stats = new StatSystem(attributes, noEffects);
            var aspects = new AspectSystem(ecs);
            return new CombatSystem(ecs, stats, attributes, aspects, rng);
        }

        private static IEffectRegistry BuildEffectRegistry()
        {
            // EffectRegistry already ships with "empower" in its built-in rows.
            return new EffectRegistry();
        }

        // ── ISessionManager stub for AffectCommand ───────────────────────────────

        private sealed class SingleSessionManager : Hedron.Core.Sessions.ISessionManager
        {
            private readonly Hedron.Core.Sessions.ISession _session;

            public SingleSessionManager(Hedron.Core.Sessions.ISession session)
                => _session = session;

            public IReadOnlyCollection<Hedron.Core.Sessions.ISession> GetAll()
                => new[] { _session };

            public Hedron.Core.Sessions.ISession? GetSession(uint playerEntityId)
                => _session.PlayerEntityId == playerEntityId ? _session : null;

            public void Register(Hedron.Core.Sessions.ISession session) { }
            public void Unregister(uint playerEntityId) { }
        }
    }
}
