using System.IO.Abstractions;

namespace Core.Packages.Installation.Installers;

internal sealed class PackageInstallerFactory(IFileSystem fileSystem, TimeProvider timeProvider) : IPackageInstallerFactory
{
    public IPackageInstaller CreateArchive(PackageMetadata package, string sourcePath) =>
        new ArchiveInstaller(fileSystem, timeProvider, package, sourcePath);

    public IPackageInstaller CreateDirectory(PackageMetadata package, string sourcePath) =>
        new DirectoryInstaller(fileSystem, timeProvider, package, sourcePath);
}
