using System.Threading.Tasks;
using Hedron.Core.Sessions;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// Routes a raw input line to the matching <see cref="ICommand"/>. Does not contain
    /// gameplay logic; it parses the verb, looks up the handler, and invokes it.
    /// </summary>
    public interface ICommandDispatcher
    {
        /// <summary>
        /// Parses <paramref name="input"/> into verb + arguments and executes the matching
        /// command. Blank input is a no-op. Unknown verbs produce a short error line on
        /// the session.
        /// </summary>
        Task DispatchAsync(ISession session, string input);
    }
}
