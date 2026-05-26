using System.Collections.Generic;

namespace Hedron.Core.Output
{
    /// <summary>
    /// Renders the equipment slot table shown by the <c>equipment</c> command.
    /// Each entry is a (SlotLabel, ItemName) pair. Always non-empty — the command writes
    /// "You are not wearing anything." when all slots are empty and skips this message.
    /// Slots are ordered by <see cref="WornSlot"/> ordinal for deterministic output.
    /// </summary>
    public sealed record EquipmentDisplayMessage(
        IReadOnlyList<(string SlotLabel, string ItemName)> Rows) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Info;
    }
}
