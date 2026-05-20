namespace Hedron.Core.Commands
{
    /// <summary>
    /// Seam for future dynamic entity-name resolution (e.g. "orc" → nearest orc entity id).
    /// Null this slice — the parser skips this field when it is null.
    /// </summary>
    public interface IArgumentResolver { }
}
