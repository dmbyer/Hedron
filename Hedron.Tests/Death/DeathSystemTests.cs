using System;
using System.Collections.Generic;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Death.Systems;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Death
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="DeathSystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/death-and-respawn.md.
    /// </summary>
    public sealed class DeathSystemTests
    {
        // ── Test doubles ─────────────────────────────────────────────────────────

        /// <summary>
        /// Hand-rolled stub for <see cref="IAttributeSystem"/> that stores pool values
        /// per-entity in memory so tests can seed and inspect them directly.
        /// HP is clamped to [HpFloor, MaxHp]; other pools to [0, Max].
        /// </summary>
        private sealed class StubAttributeSystem : IAttributeSystem
        {
            private readonly int _hpFloor;
            private readonly Dictionary<uint, PoolState> _pools = new();

            public StubAttributeSystem(int hpFloor = -10)
            {
                _hpFloor = hpFloor;
            }

            public void Seed(uint id, int maxHp = 100, int maxMana = 50, int maxStamina = 50, int maxAstra = 10)
            {
                var p = EnsurePool(id);
                p.MaxHp = maxHp; p.CurrentHp = maxHp;
                p.MaxMana = maxMana; p.CurrentMana = maxMana;
                p.MaxStamina = maxStamina; p.CurrentStamina = maxStamina;
                p.MaxAstra = maxAstra; p.CurrentAstra = maxAstra;
            }

            private PoolState EnsurePool(uint id)
            {
                if (!_pools.TryGetValue(id, out var s))
                    _pools[id] = s = new PoolState();
                return s;
            }

            // ── IAttributeSystem — attribute getters (not needed for death tests) ──
            public int GetLevel(uint id) => 1;
            public int GetMind(uint id) => 10;
            public int GetBody(uint id) => 10;
            public int GetSpirit(uint id) => 10;
            public int GetAttunement(uint id) => 10;

            // ── pool getters ────────────────────────────────────────────────────────
            public int GetMaxHp(uint id)           => EnsurePool(id).MaxHp;
            public int GetCurrentHp(uint id)       => EnsurePool(id).CurrentHp;
            public int GetMaxMana(uint id)         => EnsurePool(id).MaxMana;
            public int GetCurrentMana(uint id)     => EnsurePool(id).CurrentMana;
            public int GetMaxStamina(uint id)      => EnsurePool(id).MaxStamina;
            public int GetCurrentStamina(uint id)  => EnsurePool(id).CurrentStamina;
            public int GetMaxAstra(uint id)        => EnsurePool(id).MaxAstra;
            public int GetCurrentAstra(uint id)    => EnsurePool(id).CurrentAstra;

            // ── attribute setters (no-op for death tests) ──────────────────────────
            public void SetLevel(uint id, int v) { }
            public void SetMind(uint id, int v) { }
            public void SetBody(uint id, int v) { }
            public void SetSpirit(uint id, int v) { }
            public void SetAttunement(uint id, int v) { }
            public void SetMaxHp(uint id, int v) { EnsurePool(id).MaxHp = v; }

            public void SetCurrentHp(uint id, int v)
            {
                var p = EnsurePool(id);
                p.CurrentHp = Math.Clamp(v, _hpFloor, p.MaxHp);
            }

            public void SetMaxMana(uint id, int v)     { EnsurePool(id).MaxMana = v; }
            public void SetCurrentMana(uint id, int v)
            {
                var p = EnsurePool(id);
                p.CurrentMana = Math.Clamp(v, 0, p.MaxMana);
            }

            public void SetMaxStamina(uint id, int v)     { EnsurePool(id).MaxStamina = v; }
            public void SetCurrentStamina(uint id, int v)
            {
                var p = EnsurePool(id);
                p.CurrentStamina = Math.Clamp(v, 0, p.MaxStamina);
            }

            public void SetMaxAstra(uint id, int v)     { EnsurePool(id).MaxAstra = v; }
            public void SetCurrentAstra(uint id, int v)
            {
                var p = EnsurePool(id);
                p.CurrentAstra = Math.Clamp(v, 0, p.MaxAstra);
            }

            private sealed class PoolState
            {
                public int MaxHp = 100;      public int CurrentHp = 100;
                public int MaxMana = 50;     public int CurrentMana = 50;
                public int MaxStamina = 50;  public int CurrentStamina = 50;
                public int MaxAstra = 10;    public int CurrentAstra = 10;
            }
        }

        /// <summary>
        /// Hand-rolled stub for <see cref="IEffectSystem"/> that records which entities
        /// had impermanent effects removed, and holds a configurable list of active effects
        /// per entity so tests can seed effects and verify they are stripped on respawn.
        /// </summary>
        private sealed class TrackingEffectSystem : IEffectSystem
        {
            private readonly Dictionary<uint, List<Effect>> _effects = new();

            /// <summary>Entity ids for which RemoveImpermanent was called.</summary>
            public List<uint> RemovedImpermanentFor { get; } = new();

            public void SeedEffects(uint entityId, IEnumerable<Effect> effects)
            {
                _effects[entityId] = new List<Effect>(effects);
            }

            public IReadOnlyList<Effect> GetActive(uint entityId)
                => _effects.TryGetValue(entityId, out var list)
                    ? list.AsReadOnly()
                    : Array.Empty<Effect>();

            public void RemoveImpermanent(uint entityId)
            {
                RemovedImpermanentFor.Add(entityId);
                if (_effects.TryGetValue(entityId, out var list))
                    list.RemoveAll(e => e.Lifetime != EffectLifetime.UntilRemoved);
            }

            public EffectApplyResult Apply(uint targetEntityId, EffectDefinition definition, uint sourceEntityId)
                => EffectApplyResult.StackingBlocked;
            public void Remove(uint entityId, string effectId) { }
            public void RemoveByCategory(uint entityId, EffectCategory category) { }
            public int GetModifiers(uint entityId, ScoreId scoreId) => 0;
            public EffectTickResult AdvanceTick(TimeSpan elapsed)
                => new EffectTickResult(Array.Empty<PeriodicApplication>(), Array.Empty<(uint, Effect)>());
        }

        /// <summary>
        /// Minimal <see cref="ITemplateRegistry"/> stub that accepts a pre-seeded set of
        /// valid blueprint ids and rejects everything else.
        /// </summary>
        private sealed class StubTemplateRegistry : ITemplateRegistry
        {
            private readonly HashSet<string> _ids;

            public StubTemplateRegistry(params string[] validIds)
            {
                _ids = new HashSet<string>(validIds, StringComparer.OrdinalIgnoreCase);
            }

            public bool TryGet(string blueprintId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEntityTemplate? template)
            {
                template = null;
                return _ids.Contains(blueprintId);
            }

            public void Register(string blueprintId, IEntityTemplate template) => _ids.Add(blueprintId);
            public Entity Spawn(string blueprintId) => throw new NotImplementedException();
            public Entity Spawn(string blueprintId, IDictionary<string, object>? overrides) => throw new NotImplementedException();
            public IReadOnlyCollection<string> AllBlueprintIds() => _ids;
            public void Clear() => _ids.Clear();
        }

        // ── Factory helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds a fully wired <see cref="DeathSystem"/> using real
        /// <see cref="EntityService"/> and <see cref="EntityStateService"/>, plus
        /// stubs for the three injected domain collaborators.
        /// </summary>
        private static (
            DeathSystem system,
            EntityService ecs,
            EntityStateService entityState,
            StubAttributeSystem attributes,
            TrackingEffectSystem effects,
            StubTemplateRegistry registry,
            WorldConfiguration worldConfig
        ) Build(
            int hpFloor = -10,
            double respawnPercent = 0.25,
            string[]? validBlueprintIds = null)
        {
            var ecs         = new EntityService();
            var entityState = new EntityStateService(ecs);
            var attributes  = new StubAttributeSystem(hpFloor);
            var effects     = new TrackingEffectSystem();
            var registry    = new StubTemplateRegistry(validBlueprintIds ?? Array.Empty<string>());
            var worldConfig = new WorldConfiguration
            {
                StartingRoomEntityId   = 1u,
                StartingRoomBlueprintId = "start_room",
            };

            var options = Options.Create(new DeathOptions
            {
                HpFloor           = hpFloor,
                RespawnPoolPercent = respawnPercent,
                BleedPerTick      = 1,
            });

            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<DeathSystem>.Instance;

            var system = new DeathSystem(
                ecs,
                entityState,
                attributes,
                effects,
                registry,
                worldConfig,
                options,
                logger);

            return (system, ecs, entityState, attributes, effects, registry, worldConfig);
        }

        /// <summary>Creates a player entity (has <see cref="CharacterComponent"/>).</summary>
        private static uint MakePlayer(EntityService ecs, StubAttributeSystem attributes,
            int maxHp = 100, int maxMana = 50, int maxStamina = 50, int maxAstra = 10)
        {
            var id = new EntityBuilder(ecs).AsPlayer().Build();
            attributes.Seed(id, maxHp, maxMana, maxStamina, maxAstra);
            return id;
        }

        /// <summary>Creates a mob entity (no <see cref="CharacterComponent"/>).</summary>
        private static uint MakeMob(EntityService ecs)
            => new EntityBuilder(ecs).AsMob("goblin", new[] { "goblin" }).Build();

        // ═══════════════════════════════════════════════════════════════════════════
        // OnHpChanged — basic threshold transitions
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// HP crosses from above 0 to 0: player should become incapacitated.
        /// </summary>
        [Fact]
        public void OnHpChanged_returns_BecameIncapacitated_when_hp_crosses_zero()
        {
            var (system, ecs, entityState, attributes, _, _, _) = Build();
            var player = MakePlayer(ecs, attributes);

            var result = system.OnHpChanged(player, previousHp: 5, newHp: 0);

            Assert.Equal(DeathTransition.BecameIncapacitated, result);
            Assert.True(entityState.IsInState(player, EntityStateFlags.Incapacitated),
                "Entity should be in Incapacitated state after crossing HP zero");
        }

        /// <summary>
        /// HP drops well below 0 (e.g. overkill): still returns BecameIncapacitated
        /// when the entity was not already incapacitated, because -1 > HpFloor(-10).
        /// </summary>
        [Fact]
        public void OnHpChanged_returns_BecameIncapacitated_when_hp_drops_below_zero()
        {
            var (system, ecs, _, attributes, _, _, _) = Build();
            var player = MakePlayer(ecs, attributes);

            var result = system.OnHpChanged(player, previousHp: 3, newHp: -1);

            Assert.Equal(DeathTransition.BecameIncapacitated, result);
        }

        /// <summary>
        /// HP reaches exactly the floor (default -10): the entity dies.
        /// </summary>
        [Fact]
        public void OnHpChanged_returns_Died_when_hp_reaches_floor()
        {
            var (system, ecs, entityState, attributes, _, _, _) = Build(hpFloor: -10);
            var player = MakePlayer(ecs, attributes);
            // Put entity in Incapacitated state first (normal bleed-out path).
            entityState.TryEnterState(player, EntityStateFlags.Incapacitated, out _);

            var result = system.OnHpChanged(player, previousHp: -9, newHp: -10);

            Assert.Equal(DeathTransition.Died, result);
        }

        /// <summary>
        /// HP drops below the floor (overkill past floor): returns Died.
        /// </summary>
        [Fact]
        public void OnHpChanged_returns_Died_when_hp_drops_below_floor()
        {
            var (system, ecs, entityState, attributes, _, _, _) = Build(hpFloor: -10);
            var player = MakePlayer(ecs, attributes);
            entityState.TryEnterState(player, EntityStateFlags.Incapacitated, out _);

            var result = system.OnHpChanged(player, previousHp: -5, newHp: -15);

            Assert.Equal(DeathTransition.Died, result);
        }

        /// <summary>
        /// HP changes but remains above 0: returns None (no threshold crossed).
        /// </summary>
        [Fact]
        public void OnHpChanged_returns_None_when_hp_stays_above_zero()
        {
            var (system, ecs, _, attributes, _, _, _) = Build();
            var player = MakePlayer(ecs, attributes);

            var result = system.OnHpChanged(player, previousHp: 50, newHp: 30);

            Assert.Equal(DeathTransition.None, result);
        }

        /// <summary>
        /// HP drops to 1 (just above 0): returns None.
        /// </summary>
        [Fact]
        public void OnHpChanged_returns_None_when_hp_drops_to_one_above_zero()
        {
            var (system, ecs, _, attributes, _, _, _) = Build();
            var player = MakePlayer(ecs, attributes);

            var result = system.OnHpChanged(player, previousHp: 2, newHp: 1);

            Assert.Equal(DeathTransition.None, result);
        }

        /// <summary>
        /// HP was already 0 (entity is incapacitated) and drops to -1 (above floor): None,
        /// because previousHp was not > 0 — the crossing already happened.
        /// </summary>
        [Fact]
        public void OnHpChanged_returns_None_when_already_incapacitated_and_bleeding()
        {
            var (system, ecs, entityState, attributes, _, _, _) = Build();
            var player = MakePlayer(ecs, attributes);
            entityState.TryEnterState(player, EntityStateFlags.Incapacitated, out _);

            // Bleeding tick: -1 → -2, floor is -10.
            var result = system.OnHpChanged(player, previousHp: -1, newHp: -2);

            Assert.Equal(DeathTransition.None, result);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // OnHpChanged — only CharacterComponent entities enter the pipeline
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Mob entity (no CharacterComponent) always returns None, even when HP drops to 0.
        /// Mob death is handled by the combat system, not the death pipeline.
        /// </summary>
        [Fact]
        public void OnHpChanged_returns_None_for_mob_entity()
        {
            var (system, ecs, _, _, _, _, _) = Build();
            var mob = MakeMob(ecs);

            var result = system.OnHpChanged(mob, previousHp: 5, newHp: 0);

            Assert.Equal(DeathTransition.None, result);
        }

        /// <summary>
        /// Mob entity does not become incapacitated even when HP crosses zero.
        /// </summary>
        [Fact]
        public void OnHpChanged_mob_does_not_enter_Incapacitated_state()
        {
            var (system, ecs, entityState, _, _, _, _) = Build();
            var mob = MakeMob(ecs);

            system.OnHpChanged(mob, previousHp: 10, newHp: 0);

            Assert.False(entityState.IsInState(mob, EntityStateFlags.Incapacitated),
                "Mob entities must never enter Incapacitated state via the death pipeline");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // OnHpChanged — edge cases: exactly at threshold
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// HP drops from exactly 1 to exactly 0: BecameIncapacitated (boundary inclusive).
        /// </summary>
        [Fact]
        public void OnHpChanged_at_exactly_zero_from_one_returns_BecameIncapacitated()
        {
            var (system, ecs, _, attributes, _, _, _) = Build();
            var player = MakePlayer(ecs, attributes);

            var result = system.OnHpChanged(player, previousHp: 1, newHp: 0);

            Assert.Equal(DeathTransition.BecameIncapacitated, result);
        }

        /// <summary>
        /// HP is exactly 1 (one above the incapacitation threshold): returns None.
        /// </summary>
        [Fact]
        public void OnHpChanged_at_one_above_zero_returns_None()
        {
            var (system, ecs, _, attributes, _, _, _) = Build();
            var player = MakePlayer(ecs, attributes);

            var result = system.OnHpChanged(player, previousHp: 2, newHp: 1);

            Assert.Equal(DeathTransition.None, result);
        }

        /// <summary>
        /// HP is exactly one above the death floor (-9 when floor is -10): returns None
        /// (the entity is incapacitated but not yet dead).
        /// </summary>
        [Fact]
        public void OnHpChanged_one_above_floor_returns_None()
        {
            var (system, ecs, entityState, attributes, _, _, _) = Build(hpFloor: -10);
            var player = MakePlayer(ecs, attributes);
            entityState.TryEnterState(player, EntityStateFlags.Incapacitated, out _);

            var result = system.OnHpChanged(player, previousHp: -8, newHp: -9);

            Assert.Equal(DeathTransition.None, result);
        }

        /// <summary>
        /// HP reaches exactly the configured floor: returns Died.
        /// </summary>
        [Fact]
        public void OnHpChanged_exactly_at_floor_returns_Died()
        {
            var (system, ecs, entityState, attributes, _, _, _) = Build(hpFloor: -10);
            var player = MakePlayer(ecs, attributes);
            entityState.TryEnterState(player, EntityStateFlags.Incapacitated, out _);

            var result = system.OnHpChanged(player, previousHp: -9, newHp: -10);

            Assert.Equal(DeathTransition.Died, result);
        }

        /// <summary>
        /// Entity is already incapacitated but HP is still above zero: OnHpChanged
        /// must not return BecameIncapacitated again (no double-transition).
        /// </summary>
        [Fact]
        public void OnHpChanged_does_not_double_incapacitate_already_incapacitated_entity()
        {
            var (system, ecs, entityState, attributes, _, _, _) = Build();
            var player = MakePlayer(ecs, attributes);
            // Force entity into Incapacitated without going through the death pipeline.
            ecs.AddComponent(player, new EntityStateComponent { ActiveStates = EntityStateFlags.Incapacitated });

            // previousHp is still positive (edge case: admin forced state then HP is ticked)
            var result = system.OnHpChanged(player, previousHp: 5, newHp: 0);

            // Should return None because entity is already incapacitated.
            Assert.Equal(DeathTransition.None, result);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // OnHpChanged — incapacitation sets the Incapacitated state flag
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// After BecameIncapacitated, the entity state service reports Incapacitated.
        /// </summary>
        [Fact]
        public void OnHpChanged_BecameIncapacitated_sets_Incapacitated_state_flag()
        {
            var (system, ecs, entityState, attributes, _, _, _) = Build();
            var player = MakePlayer(ecs, attributes);

            system.OnHpChanged(player, previousHp: 10, newHp: 0);

            Assert.True(entityState.IsInState(player, EntityStateFlags.Incapacitated),
                "INV-5: DeathSystem must call IEntityStateService.TryEnterState; " +
                "entity should be Incapacitated after HP zero crossing");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Respawn — exits Incapacitated state
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// After Respawn, the entity is no longer in the Incapacitated state.
        /// </summary>
        [Fact]
        public void Respawn_exits_Incapacitated_state()
        {
            var (system, ecs, entityState, attributes, _, _, _) = Build();
            var player = MakePlayer(ecs, attributes);
            entityState.TryEnterState(player, EntityStateFlags.Incapacitated, out _);

            system.Respawn(player);

            Assert.False(entityState.IsInState(player, EntityStateFlags.Incapacitated),
                "Respawn must call ExitState(Incapacitated)");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Respawn — relocates entity to the configured respawn room
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// When the entity has a valid RespawnComponent pointing to a live room blueprint,
        /// Respawn sets LocationComponent to that room.
        /// </summary>
        [Fact]
        public void Respawn_relocates_to_configured_respawn_room()
        {
            var (system, ecs, _, attributes, _, _, _) = Build();
            var player = MakePlayer(ecs, attributes);

            // Create a live room entity with a matching BlueprintComponent.
            var roomEntity = ecs.CreateEntity();
            ecs.AddComponent(roomEntity.Id, new BlueprintComponent { BlueprintId = "respawn_room" });

            // Give the player a RespawnComponent pointing to that blueprint.
            ecs.AddComponent(player, new RespawnComponent { RoomBlueprintId = "respawn_room" });
            ecs.AddComponent(player, new LocationComponent { RoomEntityId = 0 });

            system.Respawn(player);

            var location = ecs.Get<LocationComponent>(player);
            Assert.Equal(roomEntity.Id, location.RoomEntityId);
            Assert.Equal("respawn_room", location.RoomBlueprintId);
        }

        /// <summary>
        /// When the entity has no RespawnComponent or an unresolvable blueprint,
        /// Respawn falls back to the world starting room.
        /// </summary>
        [Fact]
        public void Respawn_falls_back_to_starting_room_when_no_respawn_component()
        {
            var (system, ecs, _, attributes, _, _, worldConfig) = Build();
            var player = MakePlayer(ecs, attributes);
            ecs.AddComponent(player, new LocationComponent { RoomEntityId = 999u });

            system.Respawn(player);

            var location = ecs.Get<LocationComponent>(player);
            Assert.Equal(worldConfig.StartingRoomEntityId, location.RoomEntityId);
        }

        /// <summary>
        /// When RespawnComponent references a blueprint with no matching live entity,
        /// Respawn falls back to the world starting room.
        /// </summary>
        [Fact]
        public void Respawn_falls_back_to_starting_room_when_blueprint_has_no_live_entity()
        {
            var (system, ecs, _, attributes, _, _, worldConfig) = Build();
            var player = MakePlayer(ecs, attributes);

            // RespawnComponent references a blueprint that has no live entity in the world.
            ecs.AddComponent(player, new RespawnComponent { RoomBlueprintId = "missing_room" });
            ecs.AddComponent(player, new LocationComponent { RoomEntityId = 999u });

            system.Respawn(player);

            var location = ecs.Get<LocationComponent>(player);
            Assert.Equal(worldConfig.StartingRoomEntityId, location.RoomEntityId);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Respawn — strips impermanent effects
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Respawn calls IEffectSystem.RemoveImpermanent for the entity.
        /// </summary>
        [Fact]
        public void Respawn_calls_RemoveImpermanent_on_effect_system()
        {
            var (system, ecs, _, attributes, effects, _, _) = Build();
            var player = MakePlayer(ecs, attributes);

            system.Respawn(player);

            Assert.True(
                effects.RemovedImpermanentFor.Contains(player),
                "INV-5/death spec: Respawn must strip impermanent effects via IEffectSystem.RemoveImpermanent");
        }

        /// <summary>
        /// After Respawn, timed effects (Lifetime != UntilRemoved) are removed.
        /// UntilRemoved effects are preserved.
        /// </summary>
        [Fact]
        public void Respawn_removes_timed_effects_and_preserves_permanent_effects()
        {
            var (system, ecs, _, attributes, effects, _, _) = Build();
            var player = MakePlayer(ecs, attributes);

            // Seed both a timed effect and a permanent (UntilRemoved) effect.
            var timedEffect = MakeEffect("poison", EffectLifetime.Timed);
            var permanentEffect = MakeEffect("curse", EffectLifetime.UntilRemoved);
            effects.SeedEffects(player, new[] { timedEffect, permanentEffect });

            system.Respawn(player);

            var remaining = effects.GetActive(player);
            Assert.DoesNotContain(remaining, e => e.EffectId == "poison");
            Assert.Contains(remaining, e => e.EffectId == "curse");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Respawn — restores pools to RespawnPoolPercent of max
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// After Respawn, HP is set to floor(MaxHp * RespawnPoolPercent).
        /// Default percent is 0.25, so floor(100 * 0.25) = 25.
        /// </summary>
        [Fact]
        public void Respawn_restores_hp_to_RespawnPoolPercent_of_max()
        {
            var (system, ecs, _, attributes, _, _, _) = Build(respawnPercent: 0.25);
            var player = MakePlayer(ecs, attributes, maxHp: 100);

            system.Respawn(player);

            Assert.Equal(25, attributes.GetCurrentHp(player));
        }

        /// <summary>
        /// Respawn restores Mana to floor(MaxMana * RespawnPoolPercent).
        /// </summary>
        [Fact]
        public void Respawn_restores_mana_to_RespawnPoolPercent_of_max()
        {
            var (system, ecs, _, attributes, _, _, _) = Build(respawnPercent: 0.25);
            var player = MakePlayer(ecs, attributes, maxMana: 80);

            system.Respawn(player);

            // floor(80 * 0.25) = 20
            Assert.Equal(20, attributes.GetCurrentMana(player));
        }

        /// <summary>
        /// Respawn restores Stamina to floor(MaxStamina * RespawnPoolPercent).
        /// </summary>
        [Fact]
        public void Respawn_restores_stamina_to_RespawnPoolPercent_of_max()
        {
            var (system, ecs, _, attributes, _, _, _) = Build(respawnPercent: 0.25);
            var player = MakePlayer(ecs, attributes, maxStamina: 60);

            system.Respawn(player);

            // floor(60 * 0.25) = 15
            Assert.Equal(15, attributes.GetCurrentStamina(player));
        }

        /// <summary>
        /// Respawn restores Astra to floor(MaxAstra * RespawnPoolPercent).
        /// </summary>
        [Fact]
        public void Respawn_restores_astra_to_RespawnPoolPercent_of_max()
        {
            var (system, ecs, _, attributes, _, _, _) = Build(respawnPercent: 0.25);
            var player = MakePlayer(ecs, attributes, maxAstra: 40);

            system.Respawn(player);

            // floor(40 * 0.25) = 10
            Assert.Equal(10, attributes.GetCurrentAstra(player));
        }

        /// <summary>
        /// Pool restore uses floor(Max * percent): fractional results are floored, not rounded.
        /// e.g. floor(100 * 0.33) = floor(33.0) = 33 (not 34).
        /// </summary>
        [Fact]
        public void Respawn_pool_restore_uses_floor_not_round()
        {
            var (system, ecs, _, attributes, _, _, _) = Build(respawnPercent: 0.33);
            var player = MakePlayer(ecs, attributes, maxHp: 100);

            system.Respawn(player);

            // floor(100 * 0.33) = floor(33.0) = 33
            Assert.Equal(33, attributes.GetCurrentHp(player));
        }

        /// <summary>
        /// With a custom RespawnPoolPercent of 0.50, all pools are restored to 50% of max.
        /// </summary>
        [Fact]
        public void Respawn_uses_configured_RespawnPoolPercent()
        {
            var (system, ecs, _, attributes, _, _, _) = Build(respawnPercent: 0.50);
            var player = MakePlayer(ecs, attributes, maxHp: 100, maxMana: 50, maxStamina: 50, maxAstra: 20);

            system.Respawn(player);

            Assert.Equal(50, attributes.GetCurrentHp(player));
            Assert.Equal(25, attributes.GetCurrentMana(player));
            Assert.Equal(25, attributes.GetCurrentStamina(player));
            Assert.Equal(10, attributes.GetCurrentAstra(player));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // SetRespawn — validates blueprint exists
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// SetRespawn returns false and provides a reason when the blueprint is not registered.
        /// </summary>
        [Fact]
        public void SetRespawn_returns_false_when_blueprint_not_registered()
        {
            var (system, ecs, _, attributes, _, _, _) = Build(validBlueprintIds: Array.Empty<string>());
            var player = MakePlayer(ecs, attributes);

            var result = system.SetRespawn(player, "nonexistent_room", out var reason);

            Assert.False(result);
            Assert.NotNull(reason);
            Assert.NotEmpty(reason);
        }

        /// <summary>
        /// SetRespawn returns true and stores the blueprint id when the blueprint is registered.
        /// </summary>
        [Fact]
        public void SetRespawn_returns_true_when_blueprint_exists()
        {
            var (system, ecs, _, attributes, _, _, _) = Build(validBlueprintIds: new[] { "inn_room" });
            var player = MakePlayer(ecs, attributes);

            var result = system.SetRespawn(player, "inn_room", out var reason);

            Assert.True(result);
            Assert.Null(reason);
        }

        /// <summary>
        /// After a successful SetRespawn, the entity's RespawnComponent holds the new blueprint id.
        /// </summary>
        [Fact]
        public void SetRespawn_updates_RespawnComponent_on_success()
        {
            var (system, ecs, _, attributes, _, _, _) = Build(validBlueprintIds: new[] { "inn_room" });
            var player = MakePlayer(ecs, attributes);

            system.SetRespawn(player, "inn_room", out _);

            var respawn = ecs.Get<RespawnComponent>(player);
            Assert.Equal("inn_room", respawn.RoomBlueprintId);
        }

        /// <summary>
        /// SetRespawn overwrites an existing RespawnComponent when called a second time with a
        /// different valid blueprint.
        /// </summary>
        [Fact]
        public void SetRespawn_overwrites_existing_RespawnComponent()
        {
            var (system, ecs, _, attributes, _, _, _) = Build(
                validBlueprintIds: new[] { "old_room", "new_room" });
            var player = MakePlayer(ecs, attributes);

            system.SetRespawn(player, "old_room", out _);
            system.SetRespawn(player, "new_room", out _);

            var respawn = ecs.Get<RespawnComponent>(player);
            Assert.Equal("new_room", respawn.RoomBlueprintId);
        }

        /// <summary>
        /// A failed SetRespawn does not mutate or add a RespawnComponent.
        /// </summary>
        [Fact]
        public void SetRespawn_failure_does_not_mutate_RespawnComponent()
        {
            var (system, ecs, _, attributes, _, _, _) = Build(validBlueprintIds: new[] { "valid_room" });
            var player = MakePlayer(ecs, attributes);
            ecs.AddComponent(player, new RespawnComponent { RoomBlueprintId = "valid_room" });

            // Try to set a non-existent blueprint.
            system.SetRespawn(player, "invalid_room", out _);

            // The existing RespawnComponent must be unchanged.
            var respawn = ecs.Get<RespawnComponent>(player);
            Assert.Equal("valid_room", respawn.RoomBlueprintId);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // INV-5 guard — DeathSystem must not hold IEventBus
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// DeathSystem must not hold a field of type IEventBus (INV-5: domain systems
        /// are pure and never touch the event bus).
        /// </summary>
        [Fact]
        public void DeathSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(DeathSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: DeathSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static Effect MakeEffect(string id, EffectLifetime lifetime) => new Effect(
            EffectId: id,
            Kind: EffectKind.StatModifier,
            Params: new EffectParams(ScoreId.HpCurrent, 0),
            Category: EffectCategory.Debuff,
            Power: 1,
            Source: new EffectSource(0u),
            Group: null,
            Lifetime: lifetime,
            Duration: lifetime == EffectLifetime.Timed ? 10f : 0f,
            Elapsed: 0f,
            Stacking: StackPolicy.Stack,
            Phase: EffectPhase.Normal);
    }
}
