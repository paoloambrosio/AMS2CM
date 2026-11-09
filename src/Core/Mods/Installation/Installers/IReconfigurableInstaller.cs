using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Mods.Installation.Installers;

public interface IReconfigurableInstaller : IPackageInstaller
{
    void Reconfigure(IReadOnlySet<RootedPath> installedFiles, IBackupStrategy backupStrategy,
        ProcessingCallbacks<RootedPath> callbacks);
}
