using Hedron.Core.Sessions;

namespace Hedron.Core.Commands.Authorization
{
    /// <summary>
    /// Evaluates whether a session satisfies a single <see cref="IAuthorizationRequirement"/>.
    /// The dispatcher iterates <see cref="ICommand.RequiredPrivileges"/> and calls this for
    /// each — decoupling policy from dispatch.
    /// </summary>
    public interface IAuthorizationChecker
    {
        bool IsSatisfied(IAuthorizationRequirement requirement, ISession session);
    }
}
