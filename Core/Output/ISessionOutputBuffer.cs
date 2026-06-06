using System.Threading.Tasks;

namespace Hedron.Core.Output
{
    public interface ISessionOutputBuffer
    {
        bool HasPending { get; }
        void Enqueue(IOutputMessage message);
        Task FlushAsync();
    }
}
