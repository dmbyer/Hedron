using System;
using System.Threading.Tasks;
using Hedron.Core.Sessions;

namespace Hedron.Core.Output
{
    public interface ISessionBufferRegistry
    {
        ISessionOutputBuffer GetOrCreate(ISession session);
        void Release(Guid sessionId);
        Task FlushAllPendingAsync();
    }
}
