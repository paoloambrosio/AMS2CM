using Core.Packages.Installation.Installers;

namespace Core.Mods.Installation.Installers;

public sealed record BootfilesSource(IPackageInstaller Installer, string? EventPackageName);

public interface IBootfilesSourceFactory
{
    BootfilesSource Create(IPackageInstaller? packageInstaller);
}
