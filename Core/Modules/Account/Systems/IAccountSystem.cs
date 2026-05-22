using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Modules.Account.Systems
{
    /// <summary>
    /// Domain system for account and character lifecycle: creation, authentication,
    /// character roster management, and logout recording. Never touches the event bus.
    /// </summary>
    public interface IAccountSystem
    {
        bool UsernameExists(string username);
        bool CharacterNameExists(string characterName);

        /// <summary>Creates an account entity and returns its id.</summary>
        Task<uint> CreateAccountAsync(string username, string password, CancellationToken ct = default);

        /// <summary>Returns a success result with the account entity id, or failure.</summary>
        Task<AuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default);

        /// <summary>
        /// Creates a character entity (attaches <c>CharacterComponent</c> and
        /// <c>LocationComponent</c>), registers it on the account, and marks both dirty.
        /// Returns the new character entity id.
        /// </summary>
        Task<uint> CreateCharacterAsync(uint accountEntityId, string characterName, CancellationToken ct = default);

        /// <summary>Returns the character roster for the given account.</summary>
        IReadOnlyList<CharacterSummary> GetCharacterList(uint accountEntityId);

        /// <summary>Updates <c>LastLoginUtc</c> and marks the character entity dirty.</summary>
        void RecordLogout(uint characterEntityId);
    }

    public readonly record struct AuthResult(bool Success, uint AccountEntityId);
    public readonly record struct CharacterSummary(uint CharacterEntityId, string CharacterName);
}
