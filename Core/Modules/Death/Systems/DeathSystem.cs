using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Death.Systems
{
    /// <summary>
    /// Domain system that owns the HP-threshold evaluation, respawn mutation, and
    /// respawn-location management for the player death lifecycle.
    /// Pure: never touches the event bus or persistence (INV-5, INV-8).
    /// </summary>
    public sealed class DeathSystem : IDeathSystem
    {
        private readonly EntityService _entityService;
        private readonly IEntityStateService _entityStateService;
        private readonly IAttributeSystem _attributeSystem;
        private readonly IEffectSystem _effectSystem;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly WorldConfiguration _worldConfig;
        private readonly DeathOptions _options;
        private readonly ILogger<DeathSystem> _logger;

        public DeathSystem(
            EntityService entityService,
            IEntityStateService entityStateService,
            IAttributeSystem attributeSystem,
            IEffectSystem effectSystem,
            ITemplateRegistry templateRegistry,
            WorldConfiguration worldConfig,
            IOptions<DeathOptions> options,
            ILogger<DeathSystem> logger)
        {
            _entityService = entityService;
            _entityStateService = entityStateService;
            _attributeSystem = attributeSystem;
            _effectSystem = effectSystem;
            _templateRegistry = templateRegistry;
            _worldConfig = worldConfig;
            _options = options.Value;
            _logger = logger;
        }

        /// <inheritdoc/>
        public DeathTransition OnHpChanged(uint entityId, int previousHp, int newHp)
        {
            // Only player entities (CharacterComponent) enter the death pipeline.
            if (!_entityService.HasComponent<CharacterComponent>(entityId))
                return DeathTransition.None;

            // Died: HP is at or below the floor (and entity must be incapacitated already).
            if (newHp <= _options.HpFloor)
                return DeathTransition.Died;

            // BecameIncapacitated: crossed from positive to zero-or-below for the first time.
            if (newHp <= 0 && previousHp > 0 && !_entityStateService.IsInState(entityId, EntityStateFlags.Incapacitated))
            {
                _entityStateService.TryEnterState(entityId, EntityStateFlags.Incapacitated, out _);
                return DeathTransition.BecameIncapacitated;
            }

            return DeathTransition.None;
        }

        /// <inheritdoc/>
        public void Respawn(uint entityId)
        {
            // 1. Exit incapacitated state (and clear any other lingering runtime flags).
            _entityStateService.ExitState(entityId, EntityStateFlags.Incapacitated);

            // 2. Resolve respawn room and update LocationComponent.
            var liveBlueprints = BuildLiveBlueprintMap();

            string? targetBlueprintId = null;
            uint targetRoomEntityId = 0;

            if (_entityService.TryGet<RespawnComponent>(entityId, out var respawn) &&
                !string.IsNullOrEmpty(respawn.RoomBlueprintId) &&
                liveBlueprints.TryGetValue(respawn.RoomBlueprintId, out var resolvedId))
            {
                targetBlueprintId = respawn.RoomBlueprintId;
                targetRoomEntityId = resolvedId;
            }
            else
            {
                // Fallback: use the world starting room.
                _logger.LogWarning(
                    "DeathSystem.Respawn: entity {EntityId} has unresolvable RespawnComponent.RoomBlueprintId '{BlueprintId}'; " +
                    "falling back to starting room.",
                    entityId, respawn?.RoomBlueprintId);

                targetRoomEntityId = _worldConfig.StartingRoomEntityId;
                targetBlueprintId = _worldConfig.StartingRoomBlueprintId;
            }

            if (_entityService.TryGet<LocationComponent>(entityId, out var location))
            {
                location.RoomEntityId = targetRoomEntityId;
                location.RoomBlueprintId = targetBlueprintId;
            }
            else
            {
                _entityService.AddComponent(entityId, new LocationComponent
                {
                    RoomEntityId = targetRoomEntityId,
                    RoomBlueprintId = targetBlueprintId,
                });
            }

            // 3. Strip impermanent effects.
            _effectSystem.RemoveImpermanent(entityId);

            // 4. Restore all four pools to floor(Max * RespawnPoolPercent).
            var hpRestore = (int)Math.Floor(_attributeSystem.GetMaxHp(entityId) * _options.RespawnPoolPercent);
            var manaRestore = (int)Math.Floor(_attributeSystem.GetMaxMana(entityId) * _options.RespawnPoolPercent);
            var staminaRestore = (int)Math.Floor(_attributeSystem.GetMaxStamina(entityId) * _options.RespawnPoolPercent);
            var astraRestore = (int)Math.Floor(_attributeSystem.GetMaxAstra(entityId) * _options.RespawnPoolPercent);

            _attributeSystem.SetCurrentHp(entityId, hpRestore);
            _attributeSystem.SetCurrentMana(entityId, manaRestore);
            _attributeSystem.SetCurrentStamina(entityId, staminaRestore);
            _attributeSystem.SetCurrentAstra(entityId, astraRestore);
        }

        /// <inheritdoc/>
        public bool SetRespawn(uint entityId, string roomBlueprintId, out string? failReason)
        {
            // Validate the blueprint exists via ITemplateRegistry (per spec: use TryGet).
            if (!_templateRegistry.TryGet(roomBlueprintId, out _))
            {
                failReason = $"No blueprint with id '{roomBlueprintId}' is registered.";
                return false;
            }

            if (_entityService.TryGet<RespawnComponent>(entityId, out var existing))
            {
                existing.RoomBlueprintId = roomBlueprintId;
            }
            else
            {
                _entityService.AddComponent(entityId, new RespawnComponent { RoomBlueprintId = roomBlueprintId });
            }

            failReason = null;
            return true;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private Dictionary<string, uint> BuildLiveBlueprintMap()
        {
            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var (entityId, bp) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (!string.IsNullOrEmpty(bp.BlueprintId))
                    map[bp.BlueprintId] = entityId;
            }
            return map;
        }
    }
}
