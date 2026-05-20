namespace Hedron.Core.Commands.Authorization
{
    /// <summary>
    /// Marker for a single privilege requirement. Commands declare which requirements a
    /// caller must satisfy via <see cref="ICommand.RequiredPrivileges"/>.
    /// </summary>
    public interface IAuthorizationRequirement { }
}
