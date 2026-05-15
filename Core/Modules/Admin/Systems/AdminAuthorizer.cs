using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Sessions;
using Microsoft.Extensions.Configuration;

namespace Hedron.Core.Modules.Admin.Systems
{
    /// <summary>
    /// Default <see cref="IAdminAuthorizer"/>. Reads <c>Admin:PrivilegedNames</c> from
    /// <see cref="IConfiguration"/> and matches against the player's display name.
    /// </summary>
    /// <remarks>
    /// The component-based elevation layer (deferred — see
    /// <c>docs/use-cases/admin-privilege-elevation.md</c>) plugs in here without changing
    /// the interface: a future revision adds a check for <c>AdminPrivilegeComponent</c>
    /// alongside the settings allowlist, with the settings list still acting as the floor.
    /// </remarks>
    public sealed class AdminAuthorizer : IAdminAuthorizer
    {
        private readonly EntityService _entityService;
        private readonly HashSet<string> _privilegedNames;

        public AdminAuthorizer(EntityService entityService, IConfiguration configuration)
        {
            _entityService = entityService;

            // Manual bind avoids the Microsoft.Extensions.Configuration.Binder dependency —
            // IConfiguration is the only Configuration package referenced by Core.
            _privilegedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in configuration.GetSection("Admin:PrivilegedNames").GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(child.Value))
                    _privilegedNames.Add(child.Value);
            }
        }

        public bool IsPrivileged(ISession session) => IsPrivileged(session.PlayerEntityId);

        public bool IsPrivileged(uint playerEntityId)
        {
            if (playerEntityId == 0) return false;
            if (!_entityService.TryGet<PlayerComponent>(playerEntityId, out var player))
                return false;
            return _privilegedNames.Contains(player.DisplayName);
        }
    }
}
