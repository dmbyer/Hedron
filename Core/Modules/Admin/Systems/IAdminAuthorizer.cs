using Hedron.Core.Sessions;

namespace Hedron.Core.Modules.Admin.Systems
{
    /// <summary>
    /// Policy seam for admin command authorization. Bootstrap implementation reads from
    /// <c>Admin:PrivilegedNames</c> in configuration; a future slice will add a persisted
    /// <c>AdminPrivilegeComponent</c> layer on top — see
    /// <c>docs/use-cases/admin-privilege-elevation.md</c>.
    /// </summary>
    /// <remarks>
    /// The settings list is the floor: anyone whose display name is in
    /// <c>Admin:PrivilegedNames</c> is always admin, even without the future component.
    /// This guarantees an operator can always recover admin access by editing config.
    /// </remarks>
    public interface IAdminAuthorizer
    {
        bool IsPrivileged(ISession session);
        bool IsPrivileged(uint playerEntityId);
    }
}
