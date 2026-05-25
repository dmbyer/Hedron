using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Items.Templates;

namespace Hedron.Core.Modules.Items.Systems
{
    /// <summary>
    /// Writes <see cref="ItemTemplate"/> definitions to the content store (YAML under
    /// <c>data/content/items/</c>). Symmetric write path for <see cref="ItemTemplateDeserializer"/>.
    /// Called by admin commands that create or mutate blueprint definitions.
    /// </summary>
    public interface IItemContentWriter
    {
        Task WriteAsync(ItemTemplate template, CancellationToken ct = default);
    }
}
