using System.IO.Abstractions;

namespace Core.Utils;

/// <summary>
/// A path contained lexically within a fixed root directory.
/// Does not inspect symbolic links or junctions.
/// </summary>
public sealed record RootedPath
{
    private readonly IPath path;

    public string Root { get; }
    public string Relative { get; }
    public string Full { get; }

    public RootedPath(IDirectoryInfo root, string relative = "")
    {
        ArgumentNullException.ThrowIfNull(root);
        path = root.FileSystem.Path;
        Root = path.TrimEndingDirectorySeparator(path.GetFullPath(root.FullName));
        if (Root.Split(path.DirectorySeparatorChar, path.AltDirectorySeparatorChar).Contains(".."))
        {
            throw new ArgumentException("The root cannot contain parent-directory components.", nameof(root));
        }

        ValidateRelative(relative);
        Full = path.TrimEndingDirectorySeparator(path.GetFullPath(relative, Root));
        var rootPrefix = path.EndsInDirectorySeparator(Root) ? Root : Root + path.DirectorySeparatorChar;
        if (!StringComparer.OrdinalIgnoreCase.Equals(Full, Root) &&
            !Full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The path must stay within its root directory.", nameof(relative));
        }

        var relativePath = path.GetRelativePath(Root, Full);
        Relative = relativePath == "." ? "" : relativePath;
    }

    public RootedPath SubPath(string subPath)
    {
        ValidateRelative(subPath);
        return WithRelativePath(path.Combine(Relative, subPath));
    }

    public RootedPath WithRelativePath(string relative) =>
        new(path.FileSystem.DirectoryInfo.New(Root), relative);

    private void ValidateRelative(string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        if (path.IsPathRooted(relative))
        {
            throw new ArgumentException("The path must be relative to its root directory.", nameof(relative));
        }

        var invalidCharacters = path.GetInvalidFileNameChars();
        foreach (var component in relative.Split(path.DirectorySeparatorChar, path.AltDirectorySeparatorChar))
        {
            if (component == "." || component.Length == 0)
            {
                continue;
            }

            if (component == ".." || component.EndsWith('.') || component.EndsWith(' ') ||
                component.IndexOfAny(invalidCharacters) >= 0 || IsDeviceName(component))
            {
                throw new ArgumentException("The path contains an invalid directory or file component.",
                    nameof(relative));
            }
        }
    }

    private static bool IsDeviceName(string component)
    {
        var name = component.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
               (name.Length == 4 &&
                (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                "123456789¹²³".Contains(name[3]));
    }

    public bool Equals(RootedPath? other) =>
        other is not null &&
        StringComparer.OrdinalIgnoreCase.Equals(Full, other.Full);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Full);
}
