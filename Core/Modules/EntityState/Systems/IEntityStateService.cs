using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.EntityState.Systems
{
    /// <summary>
    /// Centralized transition-rule enforcement for entity state flags.
    /// Attaches and removes <c>EntityStateComponent</c>; validates flag combinations
    /// against a static transition table; returns structured failure reasons to callers.
    /// Never touches the event bus or persistence (INV-5).
    /// </summary>
    public interface IEntityStateService
    {
        bool TryEnterState(uint entityId, EntityStateFlags state, out string? failReason);
        void ExitState(uint entityId, EntityStateFlags state);
        bool IsInState(uint entityId, EntityStateFlags state);
        EntityStateFlags GetStates(uint entityId);
    }
}
