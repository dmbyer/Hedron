using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Systems;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Account.Systems
{
    /// <summary>
    /// Domain system for account and character lifecycle. Singleton — maintains a lazy
    /// in-memory index of usernames and character names to avoid full ECS scans on every
    /// registration attempt. Index is populated on first access and updated in-call on writes.
    /// </summary>
    /// <remarks>
    /// This system creates entities and attaches components but does not call persistence.
    /// Persistence is the responsibility of the Initiator (<c>LoginFlow</c>) after each
    /// domain method returns (INV-5: domain systems do not touch the event bus or persistence).
    /// </remarks>
    public sealed class AccountSystem : IAccountSystem
    {
        private readonly EntityService _entityService;
        private readonly IPasswordHasher _passwordHasher;
        private readonly WorldConfiguration _worldConfig;
        private readonly CharacterDefaultsOptions _characterDefaults;
        private readonly IAbilitySystem _abilitySystem;
        private readonly ILogger<AccountSystem> _logger;

        private HashSet<string>? _usernameIndex;
        private HashSet<string>? _characterNameIndex;

        public AccountSystem(
            EntityService entityService,
            IPasswordHasher passwordHasher,
            WorldConfiguration worldConfig,
            IConfiguration configuration,
            IAbilitySystem abilitySystem,
            ILogger<AccountSystem> logger)
        {
            _entityService = entityService;
            _passwordHasher = passwordHasher;
            _worldConfig = worldConfig;
            _abilitySystem = abilitySystem;
            _logger = logger;

            _characterDefaults = new CharacterDefaultsOptions();
            var section = configuration.GetSection("CharacterDefaults");
            if (int.TryParse(section["AttributeDefault"], out var attrDefault)) _characterDefaults.AttributeDefault = attrDefault;
            if (int.TryParse(section["MaxHp"], out var maxHp)) _characterDefaults.MaxHp = maxHp;
            if (int.TryParse(section["MaxMana"], out var maxMana)) _characterDefaults.MaxMana = maxMana;
            if (int.TryParse(section["MaxStamina"], out var maxStamina)) _characterDefaults.MaxStamina = maxStamina;
            if (int.TryParse(section["MaxAstra"], out var maxAstra)) _characterDefaults.MaxAstra = maxAstra;
            var abilitiesChildren = section.GetSection("StartingAbilities").GetChildren()
                .Select(c => c.Value)
                .Where(v => v != null)
                .ToArray();
            if (abilitiesChildren.Length > 0)
                _characterDefaults.StartingAbilities = abilitiesChildren!;
        }

        public bool UsernameExists(string username)
            => GetUsernameIndex().Contains(username.ToLowerInvariant());

        public bool CharacterNameExists(string characterName)
            => GetCharacterNameIndex().Contains(characterName.ToLowerInvariant());

        public Task<uint> CreateAccountAsync(string username, string password, CancellationToken ct = default)
        {
            var normalizedUsername = username.ToLowerInvariant();
            var entity = _entityService.CreateEntity();
            _entityService.AddComponent(entity.Id, new AccountComponent
            {
                Username = normalizedUsername,
                PasswordHash = _passwordHasher.Hash(password),
                CreatedAtUtc = DateTime.UtcNow,
            });
            _entityService.AddComponent(entity.Id, new PersistentEntity());

            GetUsernameIndex().Add(normalizedUsername);
            return Task.FromResult(entity.Id);
        }

        public Task<AuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default)
        {
            var normalizedUsername = username.ToLowerInvariant();
            foreach (var (entityId, account) in _entityService.GetAllComponents<AccountComponent>())
            {
                if (account.Username == normalizedUsername
                    && _passwordHasher.Verify(password, account.PasswordHash))
                    return Task.FromResult(new AuthResult(true, entityId));
            }
            return Task.FromResult(new AuthResult(false, 0));
        }

        public Task<uint> CreateCharacterAsync(uint accountEntityId, string characterName, CancellationToken ct = default)
        {
            var entity = _entityService.CreateEntity();
            var now = DateTime.UtcNow;

            _entityService.AddComponent(entity.Id, new CharacterComponent
            {
                AccountEntityId = accountEntityId,
                CharacterName = characterName,
                CreatedAtUtc = now,
                LastLoginUtc = now,
            });
            string? startingBlueprintId = null;
            if (_entityService.TryGet<BlueprintComponent>(_worldConfig.StartingRoomEntityId, out var startingBp))
                startingBlueprintId = startingBp.BlueprintId;

            _entityService.AddComponent(entity.Id, new LocationComponent
            {
                RoomEntityId = _worldConfig.StartingRoomEntityId,
                RoomBlueprintId = startingBlueprintId,
            });
            _entityService.AddComponent(entity.Id, new InventoryComponent());
            _entityService.AddComponent(entity.Id, new EquipmentComponent());
            _entityService.AddComponent(entity.Id, new AttributesComponent
            {
                Mind = _characterDefaults.AttributeDefault,
                Body = _characterDefaults.AttributeDefault,
                Spirit = _characterDefaults.AttributeDefault,
                Attunement = _characterDefaults.AttributeDefault,
            });
            _entityService.AddComponent(entity.Id, new PoolsComponent
            {
                MaxHp = _characterDefaults.MaxHp,
                CurrentHp = _characterDefaults.MaxHp,
                MaxMana = _characterDefaults.MaxMana,
                CurrentMana = _characterDefaults.MaxMana,
                MaxStamina = _characterDefaults.MaxStamina,
                CurrentStamina = _characterDefaults.MaxStamina,
                MaxAstra = _characterDefaults.MaxAstra,
                CurrentAstra = _characterDefaults.MaxAstra,
            });
            _entityService.AddComponent(entity.Id, new RespawnComponent
            {
                RoomBlueprintId = _worldConfig.StartingRoomBlueprintId,
            });
            _entityService.AddComponent(entity.Id, new AspectAffinitiesComponent());
            _entityService.AddComponent(entity.Id, new PersistentEntity());

            if (_entityService.TryGet<AccountComponent>(accountEntityId, out var account))
                account.CharacterEntityIds.Add(entity.Id);

            foreach (var abilityId in _characterDefaults.StartingAbilities)
            {
                if (!_abilitySystem.Learn(entity.Id, abilityId))
                    _logger.LogWarning("Unknown starting ability id '{AbilityId}' — skipped.", abilityId);
            }

            GetCharacterNameIndex().Add(characterName.ToLowerInvariant());
            return Task.FromResult(entity.Id);
        }

        public IReadOnlyList<CharacterSummary> GetCharacterList(uint accountEntityId)
        {
            if (!_entityService.TryGet<AccountComponent>(accountEntityId, out var account))
                return Array.Empty<CharacterSummary>();

            var list = new List<CharacterSummary>(account.CharacterEntityIds.Count);
            foreach (var charId in account.CharacterEntityIds)
            {
                if (_entityService.TryGet<CharacterComponent>(charId, out var character))
                    list.Add(new CharacterSummary(charId, character.CharacterName));
            }
            return list;
        }

        public void RecordLogout(uint characterEntityId)
        {
            if (_entityService.TryGet<CharacterComponent>(characterEntityId, out var character))
                character.LastLoginUtc = DateTime.UtcNow;
        }

        private HashSet<string> GetUsernameIndex()
        {
            if (_usernameIndex != null)
                return _usernameIndex;

            _usernameIndex = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, account) in _entityService.GetAllComponents<AccountComponent>())
                _usernameIndex.Add(account.Username);
            return _usernameIndex;
        }

        private HashSet<string> GetCharacterNameIndex()
        {
            if (_characterNameIndex != null)
                return _characterNameIndex;

            _characterNameIndex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, character) in _entityService.GetAllComponents<CharacterComponent>())
                _characterNameIndex.Add(character.CharacterName);
            return _characterNameIndex;
        }
    }
}
