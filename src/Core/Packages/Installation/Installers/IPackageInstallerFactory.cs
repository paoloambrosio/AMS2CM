namespace Core.Packages.Installation.Installers;

public interface IPackageInstallerFactory
{
    IPackageInstaller CreateArchive(PackageMetadata package, string sourcePath);
    IPackageInstaller CreateDirectory(PackageMetadata package, string sourcePath);
}
