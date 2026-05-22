using System;
using System.Collections.Generic;
using Hedron.Core.ECS;

namespace Hedron.Core.Modules.Account.Components
{
    /// <summary>
    /// Durable identity for a registered account. One per account entity.
    /// Carries the username (normalized to lowercase), a PBKDF2 password hash,
    /// and the list of character entity ids owned by this account.
    /// </summary>
    [Persistent]
    public class AccountComponent : IComponent
    {
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public List<uint> CharacterEntityIds { get; set; } = new();
        public DateTime CreatedAtUtc { get; set; }
    }
}
