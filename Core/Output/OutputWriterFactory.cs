using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    /// <summary>Creates <see cref="OutputWriter"/> instances bound to a session.</summary>
    public sealed class OutputWriterFactory : IOutputWriterFactory
    {
        public IOutputWriter Create(ISession session) => new OutputWriter(session);
    }
}
