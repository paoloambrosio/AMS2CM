using System.Collections.Immutable;
using System.IO.Abstractions;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Mods.Installation;

public class ModReconciliationService<TEventHandler>(
    IBackupStrategyProvider<DateTimeOffset, TEventHandler> backupStrategyProvider,
    IBootfilesNaming bootfilesNaming,
    IModInstallerFactory<TEventHandler> modInstallerFactory,
    IFileSystem? fileSystem = null)
    : PackageReconciliationService<TEventHandler>(backupStrategyProvider, fileSystem)
    where TEventHandler : PackageReconciliationService.IEventHandler
{
    protected override ReconciliationContext CreateReconciliationContext(
        IReadOnlyDictionary<string, PackageInstallationState> previousState,
        IEnumerable<IPackageInstaller> installers, string installDir, TEventHandler eventHandler)
    {
        var (bootfiles, notBootfiles) = installers.ToImmutableArray()
            .Partition(p => bootfilesNaming.IsBootfiles(p.PackageName));
        var bootfilesInstaller = CreateBootfilesInstaller(bootfiles, previousState, eventHandler);

        var modInstallers = notBootfiles
            .Select(i => modInstallerFactory.ModInstaller(i, bootfilesInstaller))
            .Append(bootfilesInstaller).ToImmutableArray();

        var stateWithBootfilesDepsReplaced = previousState
            .SelectValues(s => s with
            {
                Dependencies = s.Dependencies
                    .Select(d => bootfilesNaming.IsBootfiles(d) ? bootfilesInstaller.PackageName : d)
                    .ToArray()
            });

        return base.CreateReconciliationContext(stateWithBootfilesDepsReplaced, modInstallers, installDir, eventHandler);
    }

    protected override IReadOnlyCollection<IReconciliationAction> ReconciliationActions(
        string packageName, IPackageInstaller? installer, ReconciliationContext context)
    {
        if (installer is not IBootfilesInstaller bootfiles)
            return base.ReconciliationActions(packageName, installer, context);

        var previous = context.PreviousState.GetValueOrDefault(packageName);
        var needed = context.CurrentState.Any(p => p.Key != packageName && p.Value.Dependencies.Contains(packageName));
        if (!needed)
        {
            bootfiles.NotRequired();
            return previous is null ? [] : [context.Uninstall(packageName, previous)];
        }

        // Resolve generation inputs before any uninstall moves backups or installation changes them.
        _ = bootfiles.PackageVersionHash;
        return base.ReconciliationActions(packageName, installer, context)
            .Append(new BootfilesConfigurationAction(bootfiles, context.InstallDir,
                () => context.CurrentState.GetValueOrDefault(packageName), context.BackupStrategy))
            .ToArray();
    }

    protected override bool NeedsReinstall(string packageName, PackageInstallationState state,
        IPackageInstaller installer, ReconciliationContext context) =>
        base.NeedsReinstall(packageName, state, installer, context) ||
        (installer is IBootfilesInstaller && context.IsNewlyShadowed(packageName, state));

    private IBootfilesInstaller CreateBootfilesInstaller(IEnumerable<IPackageInstaller> bootfilesPackageInstallers,
        IReadOnlyDictionary<string, PackageInstallationState> previousState, TEventHandler eventHandler)
    {
        var installer = bootfilesPackageInstallers.FirstOrDefault();
        var name = installer?.PackageName ?? bootfilesNaming.GeneratedBootfilesName;
        return modInstallerFactory.BootfilesInstaller(installer, eventHandler, previousState.GetValueOrDefault(name));
    }
}
