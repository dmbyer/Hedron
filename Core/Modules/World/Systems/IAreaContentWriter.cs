using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.World.Templates;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Writes <see cref="AreaTemplate"/> definitions to the content store (YAML under
    /// <c>data/content/areas/</c>). Symmetric write path for <see cref="AreaTemplateDeserializer"/>.
    /// Called by admin commands that create or mutate area blueprint definitions.
    /// </summary>
    public interface IAreaContentWriter
    {
        Task WriteAsync(AreaTemplate template, CancellationToken ct = default);
    }
}
