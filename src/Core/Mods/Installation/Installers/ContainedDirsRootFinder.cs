using System.Collections.Immutable;
using System.IO.Abstractions;

namespace Core.Mods.Installation.Installers;

internal class ContainedDirsRootFinder : IRootFinder
{
    private readonly ImmutableHashSet<string> rootDirs;
    private readonly IPath path;

    internal ContainedDirsRootFinder(IPath path, IEnumerable<string> rootDirs)
    {
        this.path = path;
        this.rootDirs = rootDirs.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // Simplistic implementation. Not the best performance, but short and readable.
    public IRootFinder.RootPaths FromDirectoryList(IEnumerable<string> directories) =>
        directories
            .SelectMany(file =>
            {
                var dirSegments = file.Split(path.DirectorySeparatorChar);
                var segmentsUntilRoot = dirSegments.TakeWhile(_ => !rootDirs.Contains(_)).ToArray();
                return dirSegments.Length == segmentsUntilRoot.Length
                    ? Array.Empty<string>()
                    : new[] { path.Join(segmentsUntilRoot) };
            })
            .ToImmutableSortedSet()
            .Aggregate(new IRootFinder.RootPaths(path), (roots, potentialRoot) =>
                roots.AddIfAncestorNotPresent(potentialRoot)
            );
}
