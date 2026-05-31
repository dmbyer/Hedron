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
        int Mind,
        int Body,
        int Spirit,
        int Attunement,
        int CurrentMana,
        int MaxMana,
        int CurrentStamina,
        int MaxStamina,
        int CurrentAstra,
        int MaxAstra) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Info;
    }
}
