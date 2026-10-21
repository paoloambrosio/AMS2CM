using System.IO.Abstractions;

namespace Core.Utils;

public static class Paths
{
    /// <summary>
    /// Make sure that paths never end with a directory separator and always
    /// use the standard separator, and not the alternative.
    /// </summary>
    public static string NormalizeDirectorySeparators(IPath pathApi, string path) =>
        pathApi.TrimEndingDirectorySeparator(path.Replace(pathApi.AltDirectorySeparatorChar,
            pathApi.DirectorySeparatorChar));
}
