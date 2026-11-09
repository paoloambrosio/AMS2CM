using System.Collections.Immutable;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Mods.Installation;

internal sealed class ReconfigurationAction<TEventHandler>(
    IReconfigurableInstaller installer,
    Func<PackageInstallationState?> currentState,
    ReconciliationAction<TEventHandler>.Factory factory) : IReconciliationAction
    where TEventHandler : IReconciliationContext.IEventHandler
{
    private PackageInstallationState? original;
    private bool started;
    private bool complete;

    public string PackageName => installer.PackageName;
    public int? PackageVersionHash => original?.VersionHash ?? installer.PackageVersionHash;
    public DateTimeOffset InstallTime => original?.Time ?? installer.InstallTime;
    public IReadOnlySet<string> PackageDependencies => !complete && original is not null
        ? original.Dependencies.ToImmutableHashSet() : installer.PackageDependencies;
    public IReadOnlySet<string> ShadowedBy => (original ?? currentState())?.ShadowedBy.ToImmutableHashSet() ?? ImmutableHashSet<string>.Empty;
    public IInstallation.State InstallState => complete
        ? IInstallation.State.Installed : IInstallation.State.PartiallyInstalled;
    public IReadOnlySet<string> InstalledFiles => started
        ? installer.InstalledFiles.Where(p => p.Root == factory.InstallRoot).Select(p => p.Relative).ToImmutableHashSet()
        : (original?.Files ?? []).ToImmutableHashSet();

    public void Execute(ProcessingCallbacks<RootedPath> callbacks)
    {
        original = currentState();
        var ownedFiles = (original?.Files ?? []).Select(factory.InstallRoot.Combine).ToImmutableHashSet();
        var backupStrategy = factory.BackupStrategyProvider.BackupStrategy(InstallTime, factory.EventHandler);
        started = true;
        installer.Reconfigure(ownedFiles, backupStrategy, callbacks);
        complete = true;
    }
}
