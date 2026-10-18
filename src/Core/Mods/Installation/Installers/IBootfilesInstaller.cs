using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Mods.Installation.Installers;

public interface IBootfilesInstaller : IPackageInstaller
{
    // Generate staged output before uninstall can move or remove generation inputs.
    void Prepare() { }
    void Configure(IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks);
    void NotRequired();
}
