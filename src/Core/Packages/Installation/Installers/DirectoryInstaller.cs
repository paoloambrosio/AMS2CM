using System.IO.Abstractions;
using Core.Utils;

namespace Core.Packages.Installation.Installers;

internal class DirectoryInstaller : BaseDirectoryInstaller
{
    public DirectoryInstaller(string packageName, int? packageVersionHash, string sourcePath) :
        base(packageName, packageVersionHash)
    {
        Source = FileSystem.DirectoryInfo.New(sourcePath);
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
