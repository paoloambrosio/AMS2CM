using System.Collections.Immutable;
using System.IO.Abstractions;

namespace Core.Packages.Installation.Installers;

internal abstract class BaseDirectoryInstaller : BaseInstaller<IFileInfo>
{
    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        MatchType = MatchType.Win32,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        RecurseSubdirectories = true,
    };

    protected BaseDirectoryInstaller(string packageName, int? packageVersionHash) :
        this(new FileSystem(), packageName, packageVersionHash)
    {
    }

    protected BaseDirectoryInstaller(IFileSystem fs, string packageName, int? packageVersionHash) :
        base(fs, TimeProvider.System, packageName, packageVersionHash, ImmutableHashSet<string>.Empty)
    {
    }

    protected abstract IDirectoryInfo Source { get; }

    public override IEnumerable<string> RelativeDirectoryPaths =>
        Source.EnumerateDirectories("*", RecursiveEnumeration)
            .Select(_ => Path.GetRelativePath(Source.FullName, _.FullName));

    protected override void InstallAllFiles(InstallBody body)
    {
        foreach (var fileInfo in Source.EnumerateFiles("*", RecursiveEnumeration))
        {
            var relativePath = Path.GetRelativePath(Source.FullName, fileInfo.FullName);
            body(relativePath, fileInfo);
        }
    }
}
