using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Sessions;

namespace Hedron.Core.Commands.Authorization
{
    /// <summary>
    /// Default checker: pattern-matches the requirement type. <see cref="AdminRequirement"/>
    /// delegates to the existing <see cref="IAdminAuthorizer"/>. Future requirement types
    /// extend this without touching the dispatcher.
    /// </summary>
    public sealed class AuthorizationChecker : IAuthorizationChecker
    {
        private readonly IAdminAuthorizer _adminAuthorizer;

        public AuthorizationChecker(IAdminAuthorizer adminAuthorizer)
            => _adminAuthorizer = adminAuthorizer;

        public bool IsSatisfied(IAuthorizationRequirement requirement, ISession session)
            => requirement switch
            {
                AdminRequirement => _adminAuthorizer.IsPrivileged(session),
                _ => false,
            };
    }
}
