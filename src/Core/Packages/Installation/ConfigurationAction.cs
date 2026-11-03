using System.Collections.Immutable;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Packages.Installation;

internal sealed class ConfigurationAction<TEventHandler>(
    IConfigurableInstaller installer,
    Func<PackageInstallationState?> currentState,
    ReconciliationAction<TEventHandler>.Factory factory) : IReconciliationAction
    where TEventHandler : ReconciliationService.IEventHandler
{
    private PackageInstallationState? original;
    private bool complete;

    public string PackageName => installer.PackageName;
    public int? PackageVersionHash => original?.VersionHash ?? installer.PackageVersionHash;
    public DateTimeOffset InstallTime => original?.Time ?? installer.InstallTime;
    public IReadOnlySet<string> PackageDependencies => installer.PackageDependencies;
    public IReadOnlySet<string> ShadowedBy => (original ?? currentState())?.ShadowedBy.ToImmutableHashSet() ?? ImmutableHashSet<string>.Empty;
    public IInstallation.State InstallState => complete
        ? IInstallation.State.Installed : IInstallation.State.PartiallyInstalled;
    public IReadOnlySet<string> InstalledFiles => (original?.Files ?? [])
        .Concat(installer.InstalledFiles.Where(p => p.Root == factory.InstallRoot).Select(p => p.Relative))
        .ToImmutableHashSet();

    public void Execute(ProcessingCallbacks<RootedPath> callbacks)
    {
        original = currentState();
        installer.Configure(factory.BackupStrategyProvider.BackupStrategy(InstallTime, factory.EventHandler), callbacks);
        complete = true;
    }
}
