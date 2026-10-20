using System.IO.Abstractions;
using Core.Packages.Installation.Installers;

namespace Core.Mods.Installation.Installers;

public class ModInstallerFactory<TConfig>(
    IFileSystem fileSystem,
    IRootFinder rootFinder,
    TConfig config,
    InstallationPaths paths,
    IBootfilesSourceFactory bootfilesSourceFactory) : IModInstallerFactory<BootfilesInstaller.IEventHandler>
    where TConfig : ModInstaller.IConfig, BootfilesInstaller.IConfig
{
    public IPackageInstaller ModInstaller(IPackageInstaller packageInstaller, IPackageInstaller bootfilesInstaller) =>
        new ModInstaller(fileSystem, rootFinder, packageInstaller,
            new ModInstallerOptions(paths, config, bootfilesInstaller.PackageName));

    public IPackageInstaller BootfilesInstaller(IPackageInstaller? bootfilesPackageInstaller,
        BootfilesInstaller.IEventHandler eventHandler)
    {
        var source = bootfilesSourceFactory.Create(bootfilesPackageInstaller);
        return new BootfilesInstaller(fileSystem, rootFinder, source.Installer,
            new BootfilesInstallerOptions(paths, config, source.EventPackageName), eventHandler);
    }
}
