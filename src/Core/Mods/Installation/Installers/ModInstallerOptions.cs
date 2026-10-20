namespace Core.Mods.Installation.Installers;

public sealed record ModInstallerOptions(
    InstallationPaths Paths,
    ModInstaller.IConfig Config,
    string BootfilesPackageName);
