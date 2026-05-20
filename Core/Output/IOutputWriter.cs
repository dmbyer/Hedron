using System.Threading.Tasks;

namespace Hedron.Core.Output
{
    /// <summary>
    /// Sends a typed <see cref="IOutputMessage"/> to a recipient.
    /// Slice-3 impl: stringify-and-forward via <c>ISession.SendLineAsync</c>.
    /// Slice 4 replaces the implementation with a formatter-backed one.
    /// </summary>
    public interface IOutputWriter
    {
        Task WriteAsync(IOutputMessage message);
    }
}
