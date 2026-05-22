using System;
using Hedron.Core.ECS;

namespace Hedron.Core.Modules.Account.Components
{
    /// <summary>
    /// Durable identity for a player character. One per character entity.
    /// Carries the owning account id, character name, and login timestamps.
    /// </summary>
    [Persistent]
    public class CharacterComponent : IComponent
    {
        public uint AccountEntityId { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime LastLoginUtc { get; set; }
    }
}
