namespace Hedron.Core
{
    /// <summary>
    /// Named equipment slots a character can fill. Two-of-a-kind physical slots (rings, wrists)
    /// are distinct enum values rather than indexed entries — <see cref="Hedron.Core.ECS.Components.EquipmentComponent"/>
    /// holds one item per value. Adding a slot is a pure enum + YAML extension; no model change.
    /// </summary>
    public enum WornSlot
    {
        MainHand,
        OffHand,
        Head,
        Chest,
        Feet,
        Legs,
        Hands,
        Arms,
        Waist,
        Neck,
        Finger,
        Finger2,
        Wrist,
        Wrist2,
    }
}
