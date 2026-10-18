using Core.Packages.Installation;
using Core.Packages.Installation.Installers;

namespace Core.Mods.Installation.Installers;

public interface IModInstallerFactory<in TEventHandler>
{
    public IPackageInstaller ModInstaller(IPackageInstaller packageInstaller, IPackageInstaller bootfilesInstaller);
    public IBootfilesInstaller BootfilesInstaller(IPackageInstaller? bootfilesPackageInstaller, TEventHandler eventHandler,
        PackageInstallationState? previousInstallation = null);
}
