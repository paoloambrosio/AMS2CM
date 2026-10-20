using System.IO.Abstractions;
using Core.Packages.Installation.Installers;

namespace Core.Mods.Installation.Installers;

internal sealed class BootfilesSourceFactory(
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    InstallationPaths paths,
    IBootfilesNaming bootfilesNaming) : IBootfilesSourceFactory
{
    public BootfilesSource Create(IPackageInstaller? packageInstaller)
    {
        var installer = packageInstaller ?? new GeneratedBootfilesInstaller(
            fileSystem, timeProvider, new PackageMetadata(bootfilesNaming.GeneratedBootfilesName, null), paths);
        return new BootfilesSource(installer,
            bootfilesNaming.IsGeneratedBootfiles(installer.PackageName) ? installer.PackageName : null);
    }
}
