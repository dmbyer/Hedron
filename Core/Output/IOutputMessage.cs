namespace Hedron.Core.Output
{
    /// <summary>Typed message emitted by a command or system. Slice 4 routes through a formatter.</summary>
    public interface IOutputMessage
    {
        OutputCategory Category { get; }
    }
}
