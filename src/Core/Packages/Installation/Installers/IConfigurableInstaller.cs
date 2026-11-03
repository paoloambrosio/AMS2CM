using Core.Packages.Installation.Backup;
using Core.Utils;

namespace Core.Packages.Installation.Installers;

public interface IConfigurableInstaller : IPackageInstaller
{
    void Configure(IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks);
}
