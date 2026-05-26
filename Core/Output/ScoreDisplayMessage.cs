namespace Hedron.Core.Output
{
    /// <summary>
    /// Carries the stat block written by the <c>score</c> command.
    /// </summary>
    public sealed record ScoreDisplayMessage(
        string CharacterName,
        int Level,
        int CurrentHp,
        int MaxHp,
        int Strength,
        int Dexterity,
        int Constitution) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Info;
    }
}
