namespace Hedron.Core.Modules.Regeneration.Systems
{
    /// <summary>
    /// Domain system for baseline resource regeneration.
    /// Iterates all entities with pools and applies per-tick HP/Mana/Stamina/Astra deltas
    /// based on entity state. Never touches the event bus or persistence (INV-5).
    /// </summary>
    public interface IRegenerationSystem
    {
        void ApplyTickRegen(long tickId);
    }
}
