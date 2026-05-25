using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.World.Templates;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Writes <see cref="RoomTemplate"/> definitions to the content store (YAML under
    /// <c>data/content/rooms/</c>). Symmetric write path for <see cref="RoomTemplateDeserializer"/>.
    /// Called by admin commands that create or mutate room blueprint definitions, and by
    /// <see cref="WorldContentLoader"/> when seeding the void room for the first time.
    /// </summary>
    public interface IRoomContentWriter
    {
        Task WriteAsync(RoomTemplate template, CancellationToken ct = default);
    }
}
