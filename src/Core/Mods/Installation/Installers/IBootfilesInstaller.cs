using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Mods.Installation.Installers;

public interface IBootfilesInstaller : IPackageInstaller
{
    // Generate staged output before uninstall can move or remove generation inputs.
    void Prepare() { }
    void Configure(IReadOnlyList<PackageInstallationState> modsInPriorityOrder, IBackupStrategy backupStrategy,
        ProcessingCallbacks<RootedPath> callbacks);
    void NotRequired();
}
