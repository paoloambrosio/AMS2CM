using System.Runtime.Versioning;

namespace Core.IO;

/// <summary>
/// Placeholder for <see cref="ISafeFileDelete"/> on freedesktop.org systems.
/// </summary>
/// <seealso href="https://specifications.freedesktop.org/trash-spec/trashspec-latest.html">FreeDesktop Trash Specification</seealso>
[SupportedOSPlatform("linux")]
public class FreeDesktopTrash : ISafeFileDelete
{
    public void SafeDelete(string filePath) =>
        throw new NotImplementedException("Safe deletion is not implemented on Linux.");
}
