using System.IO.Abstractions;

namespace Core.Mods.Installation.Installers;

public interface IRootFinder
{
    RootPaths FromDirectoryList(IEnumerable<string> directories);

    /// <remarks>
    /// This could be made mutable using a builder pattern, but it wasn't
    /// because of how simple it is and how it is used in the software.
    /// </remarks>
    public record RootPaths(IPath path)
    {
        private IPath PathApi { get; } = path;
        internal List<string> Roots = new();

        public string? GetPathFromRoot(string path)
        {
            foreach (var root in Roots)
            {
                // Empty path is ancestor of any path
                if (root.Length == 0)
                {
                    return path;
                }

                // Adding directory separator prevents substring match in directory name
                if ($"{path}{PathApi.DirectorySeparatorChar}".StartsWith($"{root}{PathApi.DirectorySeparatorChar}"))
                {
                    return PathApi.GetRelativePath(root, path);
                }
            }

            return null;
        }

        public RootPaths AddIfAncestorNotPresent(string path)
        {
            if (GetPathFromRoot(path) is null)
            {
                Roots.Add(path);
            }

            return this;
        }
    }
}
