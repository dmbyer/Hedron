using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.EntityState.Systems
{
    public sealed class EntityStateService : IEntityStateService
    {
        private readonly EntityService _entityService;

        // Static transition rule table: state to enter → blocking flags with player-facing messages.
        private static readonly Dictionary<EntityStateFlags, (EntityStateFlags Blocking, string FailReason)[]> _rules = new()
        {
            [EntityStateFlags.Resting] =
            [
                (EntityStateFlags.InCombat,      "You cannot rest while in combat."),
                (EntityStateFlags.Incapacitated, "You cannot rest while incapacitated."),
            ],
            [EntityStateFlags.InCombat] =
            [
                (EntityStateFlags.Incapacitated, "You cannot enter combat while incapacitated."),
            ],
        };

        public EntityStateService(EntityService entityService)
        {
            _entityService = entityService;
        }

        public bool TryEnterState(uint entityId, EntityStateFlags state, out string? failReason)
        {
            var current = GetStates(entityId);

            if (_rules.TryGetValue(state, out var checks))
            {
                foreach (var (blocking, reason) in checks)
                {
                    if ((current & blocking) != 0)
                    {
                        failReason = reason;
                        return false;
                    }
                }
            }

            if (_entityService.TryGet<EntityStateComponent>(entityId, out var component))
            {
                component.ActiveStates |= state;
            }
            else
            {
                _entityService.AddComponent(entityId, new EntityStateComponent { ActiveStates = state });
            }

            failReason = null;
            return true;
        }

        public void ExitState(uint entityId, EntityStateFlags state)
        {
            if (!_entityService.TryGet<EntityStateComponent>(entityId, out var component))
                return;

            component.ActiveStates &= ~state;

            if (component.ActiveStates == EntityStateFlags.None)
                _entityService.RemoveComponent<EntityStateComponent>(entityId);
        }

        public bool IsInState(uint entityId, EntityStateFlags state)
            => (GetStates(entityId) & state) != 0;

        public EntityStateFlags GetStates(uint entityId)
            => _entityService.TryGet<EntityStateComponent>(entityId, out var c) ? c.ActiveStates : EntityStateFlags.None;
    }
}
