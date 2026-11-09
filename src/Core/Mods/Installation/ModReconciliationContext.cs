using System.Collections.Immutable;
using Core.Games;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Mods.Installation;

public class ModReconciliationContext<TEventHandler, TConfig>(
    IReadOnlyDictionary<string, PackageInstallationState> previousState,
    IGame game,
    ITempDir tempDir,
    IBootfilesNaming bootfilesNaming,
    TConfig config) : PackageReconciliationContext<TEventHandler>(previousState)
    where TEventHandler : ModReconciliationContext.IEventHandler
    where TConfig : ModInstaller.IConfig, BootfilesInstaller.IConfig
{
    public static IReconciliationContext<TEventHandler>.FactoryFunc Factory(
        IGame game,
        ITempDir tempDir,
        IBootfilesNaming bootfilesNaming,
        TConfig config) => previousState =>
        new ModReconciliationContext<TEventHandler, TConfig>(
            previousState, game, tempDir, bootfilesNaming, config);

    private protected override IReadOnlyCollection<Action> CreateActions(
        PlanningInputs inputs, ReconciliationAction<TEventHandler>.Factory actionFactory)
    {
        var (bootfilesPackages, notBootfiles) = inputs.Installers.ToImmutableArray()
            .Partition(p => bootfilesNaming.IsBootfiles(p.PackageName));
        var bootfiles = CreateBootfilesInstaller(bootfilesPackages.FirstOrDefault(), actionFactory.EventHandler);

        var modActions = CreateModActions(inputs, actionFactory, notBootfiles, bootfiles);
        return [.. modActions, () => ReconcileBootfiles(inputs, actionFactory, bootfiles)];
    }

    private IEnumerable<Action> CreateModActions(PlanningInputs inputs,
        ReconciliationAction<TEventHandler>.Factory actionFactory,
        IEnumerable<IPackageInstaller> notBootfiles, IBootfilesInstaller bootfiles)
    {
        var modInstallers = notBootfiles
            .Select(i => CreateModInstaller(i, bootfiles))
            .ToArray();
        var installersByName = modInstallers.ToDictionary(i => i.PackageName);

        var planningState = RemapBootfilesDependencies(inputs, bootfiles);

        var ordinaryInputs = new PlanningInputs(modInstallers,
            planningState.Where(p => p.Key != bootfiles.PackageName).ToDictionary());
        return CreatePackageActions(ordinaryInputs, actionFactory)
            .Select(packageAction => (Action)(() => ReconcileMod(packageAction, installersByName, actionFactory)));
    }

    private void ReconcileMod(PackageAction packageAction,
        Dictionary<string, IReconfigurableInstaller> installersByName,
        ReconciliationAction<TEventHandler>.Factory actionFactory)
    {
        packageAction.Execute();
        if (installersByName.TryGetValue(packageAction.PackageName, out var installer) &&
            installer.Installed != IInstallation.State.Installed &&
            CurrentState.ContainsKey(installer.PackageName))
        {
            Reconfigure(actionFactory, installer);
        }
    }

    private IReadOnlyDictionary<string, PackageInstallationState> RemapBootfilesDependencies(
        PlanningInputs inputs, IBootfilesInstaller bootfiles) =>
        inputs.PlanningState
            .SelectValues(s => s with
            {
                Dependencies = s.Dependencies
                    .Select(d => bootfilesNaming.IsBootfiles(d) ? bootfiles.PackageName : d)
                    .ToArray()
            });

    private void ReconcileBootfiles(PlanningInputs inputs,
        ReconciliationAction<TEventHandler>.Factory actionFactory, IBootfilesInstaller bootfiles)
    {
        var state = CurrentState.GetValueOrDefault(bootfiles.PackageName);
        if (!BootfilesRequired(bootfiles))
        {
            RemoveBootfiles(actionFactory, bootfiles, state);
            return;
        }

        bootfiles.SelectSources(CurrentState);
        var reinstall = RequiresBootfilesReinstall(inputs, bootfiles, state);
        if (reinstall)
        {
            bootfiles.Prepare(CurrentState);
        }

        var actions = CreateBootfilesActions(actionFactory, bootfiles, reinstall, state);
        foreach (var action in actions)
        {
            ExecuteAction(action);
        }
        if (!reinstall)
        {
            Reconfigure(actionFactory, bootfiles);
        }
    }

    private bool BootfilesRequired(IBootfilesInstaller bootfiles) =>
        CurrentState.Any(p => p.Key != bootfiles.PackageName &&
            p.Value.Dependencies.Contains(bootfiles.PackageName));

    private void RemoveBootfiles(ReconciliationAction<TEventHandler>.Factory actionFactory,
        IBootfilesInstaller bootfiles, PackageInstallationState? state)
    {
        bootfiles.NotRequired();
        if (state is not null)
        {
            ExecuteAction(actionFactory.Uninstall(bootfiles.PackageName, state));
        }
    }

    private bool RequiresBootfilesReinstall(PlanningInputs inputs, IBootfilesInstaller bootfiles,
        PackageInstallationState? state)
    {
        var previous = inputs.PlanningState.GetValueOrDefault(bootfiles.PackageName);
        return state is null || state.Partial || state.VersionHash is null ||
            bootfiles.PackageVersionHash is null || state.VersionHash != bootfiles.PackageVersionHash ||
            state.ShadowedBy.Any(ReplacedPackages.Contains) ||
            state.ShadowedBy.Any(p => !CurrentState.ContainsKey(p)) ||
            (previous is not null && !previous.ShadowedBy.ToHashSet().SetEquals(state.ShadowedBy));
    }

    private static List<IReconciliationAction> CreateBootfilesActions(
        ReconciliationAction<TEventHandler>.Factory actionFactory,
        IBootfilesInstaller bootfiles, bool reinstall, PackageInstallationState? state)
    {
        List<IReconciliationAction> actions = [];
        if (reinstall && state is not null)
        {
            actions.Add(actionFactory.Uninstall(bootfiles.PackageName, state));
        }
        actions.Add(reinstall || state is null
            ? actionFactory.Install(bootfiles)
            : actionFactory.Keep(bootfiles.PackageName, state));
        return actions;
    }

    private void Reconfigure(ReconciliationAction<TEventHandler>.Factory actionFactory,
        IReconfigurableInstaller installer) =>
        ExecuteAction(new ReconfigurationAction<TEventHandler>(installer,
                () => CurrentState.GetValueOrDefault(installer.PackageName), actionFactory),
            allowUpdatingOwnedFiles: true);

    protected virtual IReconfigurableInstaller CreateModInstaller(IPackageInstaller packageInstaller,
        IPackageInstaller bootfilesInstaller) =>
        new ModInstaller(packageInstaller, tempDir.BaseDirectory, config, game.InstallationDirectory,
            bootfilesInstaller.PackageName);

    protected virtual IBootfilesInstaller CreateBootfilesInstaller(IPackageInstaller? bootfilesPackageInstaller,
        TEventHandler eventHandler) =>
        new BootfilesInstaller(bootfilesPackageInstaller, tempDir.BaseDirectory, config,
            game.InstallationDirectory, bootfilesNaming, eventHandler);
}

public static class ModReconciliationContext
{
    public interface IEventHandler : IReconciliationContext.IEventHandler, BootfilesInstaller.IEventHandler
    {
    }
}
