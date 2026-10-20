using System.IO.Abstractions;
using Core.Utils;

namespace Core.Packages.Installation.Installers;

internal class DirectoryInstaller : BaseDirectoryInstaller
{
    public DirectoryInstaller(IFileSystem fileSystem, TimeProvider timeProvider, PackageMetadata package,
        string sourcePath) :
        base(fileSystem, timeProvider, package)
    {
        Source = fileSystem.DirectoryInfo.New(sourcePath);
    }

    protected override IDirectoryInfo Source
    {
        get;
    }

    protected override void InstallFile(RootedPath destinationPath, IFileInfo fileInfo)
    {
        FileSystem.File.Copy(fileInfo.FullName, destinationPath.Full, overwrite: true);
    }
}
