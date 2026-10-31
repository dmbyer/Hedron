namespace Hedron.Core.Systems.Definitions
{
    /// <summary>
    /// One file in a <see cref="YamlDefinitionDirectory{TDto}"/> listing.
    /// </summary>
    /// <param name="Path">Full path of the file.</param>
    /// <param name="FileName">The file name with extension.</param>
    /// <param name="Name">The name the family's selector returned, falling back to the file-name stem.</param>
    /// <param name="Error">Non-null when the file could not be read or parsed; it is still listed.</param>
    public sealed record YamlFileEntry(string Path, string FileName, string Name, string? Error);
}
