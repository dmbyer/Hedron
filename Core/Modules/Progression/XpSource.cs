namespace Hedron.Core.Modules.Progression
{
    /// <summary>
    /// The stable key for an experience award's origin. Slice 1 wires only
    /// <see cref="CombatKill"/>; the rest are declared now so the <c>AwardExperience</c> signature
    /// is stable when later sources land (books, trainers, objectives — see the program brief's
    /// "Advancement triggers" design note).
    /// </summary>
    public enum XpSource
    {
        CombatKill,
        Book,
        Trainer,
        Objective,
    }
}
