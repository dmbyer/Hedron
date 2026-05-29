namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Author-facing metadata grouping a set of rooms.
    /// Areas are entities; rooms reference their area by blueprint id at authoring time.
    /// </summary>
    public class AreaComponent : IComponent
    {
        public string AreaId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int RespawnRate { get; set; }
        public bool Pvp { get; set; }
    }
}
