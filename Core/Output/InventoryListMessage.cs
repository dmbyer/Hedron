using System.Collections.Generic;

namespace Hedron.Core.Output
{
    /// <summary>
    /// Lists the items carried by the player. Always non-empty — the command writes a
    /// plain "You are carrying nothing." when the inventory is empty and skips this message.
    /// </summary>
    public sealed record InventoryListMessage(IReadOnlyList<string> ItemNames) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Info;
    }
}
