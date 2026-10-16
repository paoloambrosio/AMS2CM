using Core.Packages.Installation.Backup;
using Core.Utils;

namespace Core.Packages.Installation.Installers;

/// <summary>
/// A package whose configuration can be refreshed after the other packages have been reconciled.
/// </summary>
public interface IReconfigurablePackageInstaller : IPackageInstaller
{
    bool IsRequired { get; }

    // Existing files have already passed ownership checks. New files must use callbacks,
    // including Before before writing, so failed writes remain tracked for recovery.
    void Configure(IReadOnlySet<RootedPath> installedFiles, IBackupStrategy backupStrategy,
        ProcessingCallbacks<RootedPath> callbacks);
}
