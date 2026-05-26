using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Mobs.Templates;

namespace Hedron.Core.Modules.Mobs.Systems
{
    public interface IMobContentWriter
    {
        Task WriteAsync(MobTemplate template, CancellationToken ct = default);
    }
}
