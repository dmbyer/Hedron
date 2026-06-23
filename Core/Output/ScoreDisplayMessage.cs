using System.Collections.Generic;
using Hedron.Core.Modules.Economy;

namespace Hedron.Core.Output
{
    /// <summary>
    /// Carries the stat block written by the <c>score</c> command.
    /// </summary>
    /// <param name="WalletBalances">
    /// Raw <c>CurrencyId → baseAmount</c> pairs from <c>IWalletSystem.GetBalances</c>.
    /// Empty if the player has no <c>WalletComponent</c>. Formatting (ladder display) is
    /// performed by <c>TelnetOutputFormatter</c> reading <c>ICurrencyRegistry</c>.
    /// </param>
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
        int MaxAstra,
        string? RespawnRoomBlueprintId = null,
        bool IsIncapacitated = false,
        IReadOnlyDictionary<CurrencyId, long>? WalletBalances = null) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Info;
    }
}
