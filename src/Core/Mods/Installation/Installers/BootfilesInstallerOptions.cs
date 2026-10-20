namespace Core.Mods.Installation.Installers;

public sealed record BootfilesInstallerOptions(
    InstallationPaths Paths,
    BootfilesInstaller.IConfig Config,
    string? EventPackageName);
