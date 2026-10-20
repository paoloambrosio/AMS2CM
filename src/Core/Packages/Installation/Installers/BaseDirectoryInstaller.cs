using System.IO.Abstractions;

namespace Core.Packages.Installation.Installers;

internal abstract class BaseDirectoryInstaller : BaseInstaller<IFileInfo>
{
    protected BaseDirectoryInstaller(IFileSystem fileSystem, TimeProvider timeProvider, PackageMetadata package) :
        base(fileSystem, timeProvider, package)
    {
    }

    protected abstract IDirectoryInfo Source { get; }

    public override IEnumerable<string> RelativeDirectoryPaths =>
        VisibleDirectories().Skip(1)
            .Select(_ => Path.GetRelativePath(Source.FullName, _.FullName));

    protected override void InstallAllFiles(InstallBody body)
    {
        foreach (var directory in VisibleDirectories())
        {
            foreach (var fileInfo in directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly).Where(IsVisible))
            {
                var relativePath = Path.GetRelativePath(Source.FullName, fileInfo.FullName);
                body(relativePath, fileInfo);
            }
        }
    }

    private IEnumerable<IDirectoryInfo> VisibleDirectories()
    {
        var directories = new Queue<IDirectoryInfo>();
        directories.Enqueue(Source);
        while (directories.TryDequeue(out var directory))
        {
            yield return directory;
            foreach (var child in directory.EnumerateDirectories("*", SearchOption.TopDirectoryOnly).Where(IsVisible))
            {
                directories.Enqueue(child);
            }
        }
    }

    private static bool IsVisible(IFileSystemInfo info) =>
        (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
}
