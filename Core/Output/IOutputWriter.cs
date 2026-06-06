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
        Task FlushAsync();
        /// <summary>
        /// Signals that the next <see cref="FlushAsync"/> call (command-end) should be
        /// skipped so output accumulates in the session buffer until the tick-end flush.
        /// Used by in-combat ability invocations to batch their output with the round.
        /// </summary>
        void DeferFlush();
    }
}
