using System.Collections.Immutable;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Mods.Installation;

internal sealed class BootfilesConfigurationAction(
    IBootfilesInstaller installer,
    string installDir,
    Func<PackageInstallationState?> currentState,
    Func<DateTimeOffset, IBackupStrategy> backupStrategy) : IReconciliationAction
{
    private PackageInstallationState? state;
    private HashSet<RootedPath> files = [];
    private PackageInstallationState? Installation => state ?? currentState();

    public required Func<IReadOnlyList<PackageInstallationState>> CurrentModsInPriorityOrder { get; init; }

    public string PackageName => installer.PackageName;
    public int? PackageVersionHash => Installation?.VersionHash;
    public int InstallationFormatVersion => Installation?.InstallationFormatVersion ?? installer.InstallationFormatVersion;
    public DateTimeOffset InstallTime => Installation?.Time ?? installer.InstallTime;
    public IReadOnlySet<string> PackageDependencies => Installation?.Dependencies.ToImmutableHashSet() ?? [];
    public IReadOnlySet<string> ShadowedBy => Installation?.ShadowedBy.ToImmutableHashSet() ?? [];
    public IReadOnlySet<string> InstalledFiles => files.Select(p => p.Relative).ToImmutableHashSet();
    public IInstallation.State InstallState { get; private set; } = IInstallation.State.NotInstalled;

    public void Execute(ProcessingCallbacks<RootedPath> callbacks)
    {
        state = currentState();
        if (state is null) return;

        files = state.Files.Select(p => new RootedPath(installDir, p)).ToHashSet();
        InstallState = IInstallation.State.PartiallyInstalled;
        installer.Configure(CurrentModsInPriorityOrder(), backupStrategy(state.Time), callbacks with
        {
            Accept = path => files.Contains(path) || callbacks.Accept(path),
            Before = path =>
            {
                if (files.Add(path)) callbacks.Before(path);
            }
        });
        InstallState = IInstallation.State.Installed;
    }
}
