namespace Hedron.Core.Commands.Authorization
{
    /// <summary>Requires the caller to be a privileged admin session.</summary>
    public sealed record AdminRequirement : IAuthorizationRequirement;
}
