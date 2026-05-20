using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    /// <summary>Creates an <see cref="IOutputWriter"/> bound to a session.</summary>
    public interface IOutputWriterFactory
    {
        IOutputWriter Create(ISession session);
    }
}
