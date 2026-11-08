using System.Collections.Immutable;
using Core.Games;
using Core.Mods.Installation.Installers;
using Core.Packages;
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

    protected override PlanningInputs CreatePlanningInputs(IEnumerable<IPackage> packages,
        TEventHandler eventHandler)
    {
        var inputs = base.CreatePlanningInputs(packages, eventHandler);
        var (bootfiles, notBootfiles) = inputs.Installers.ToImmutableArray()
            .Partition(p => bootfilesNaming.IsBootfiles(p.PackageName));
        var bootfilesInstaller = CreateBootfilesInstaller(bootfiles.FirstOrDefault(), eventHandler);

        var modInstallers = notBootfiles
            .Select(i => CreateModInstaller(i, bootfilesInstaller))
            .Append(bootfilesInstaller);

        var planningState = inputs.PlanningState
            .SelectValues(s => s with
            {
                Dependencies = s.Dependencies
                    .Select(d => bootfilesNaming.IsBootfiles(d) ? bootfilesInstaller.PackageName : d)
                    .ToArray()
            });

        return new PlanningInputs(modInstallers.ToArray(), planningState);
    }

    protected virtual IPackageInstaller CreateModInstaller(IPackageInstaller packageInstaller,
        IPackageInstaller bootfilesInstaller) =>
        new ModInstaller(packageInstaller, tempDir.BaseDirectory, config, game.InstallationDirectory,
            bootfilesInstaller.PackageName);

    private protected override IReadOnlyCollection<Action> CreateActions(
        PlanningInputs inputs, ReconciliationAction<TEventHandler>.Factory actionFactory)
    {
        var bootfiles = inputs.Installers[^1];
        var ordinaryInputs = new PlanningInputs(inputs.Installers.SkipLast(1).ToArray(),
            inputs.PlanningState.Where(p => p.Key != bootfiles.PackageName).ToDictionary());
        return [.. base.CreateActions(ordinaryInputs, actionFactory), () =>
        {
            var state = CurrentState.GetValueOrDefault(bootfiles.PackageName);
            var required = CurrentState.Any(p => p.Key != bootfiles.PackageName &&
                p.Value.Dependencies.Contains(bootfiles.PackageName));
            if (!required)
            {
                (bootfiles as BootfilesInstaller)?.NotRequired();
                if (state is not null)
                {
                    ExecuteAction(actionFactory.Uninstall(bootfiles.PackageName, state));
                }
                return;
            }

            (bootfiles as BootfilesInstaller)?.SelectSources(CurrentState);
            var previous = inputs.PlanningState.GetValueOrDefault(bootfiles.PackageName);
            var reinstall = state is null || state.Partial || state.VersionHash is null ||
                bootfiles.PackageVersionHash is null || state.VersionHash != bootfiles.PackageVersionHash ||
                state.ShadowedBy.Any(ReplacedPackages.Contains) ||
                state.ShadowedBy.Any(p => !CurrentState.ContainsKey(p)) ||
                (previous is not null && !previous.ShadowedBy.ToHashSet().SetEquals(state.ShadowedBy));
            if (reinstall)
            {
                (bootfiles as BootfilesInstaller)?.Prepare(CurrentState);
            }

            List<IReconciliationAction> actions = [];
            if (reinstall && state is not null)
            {
                actions.Add(actionFactory.Uninstall(bootfiles.PackageName, state));
            }
            actions.Add(reinstall || state is null
                ? actionFactory.Install(bootfiles)
                : actionFactory.Keep(bootfiles.PackageName, state));
            if (bootfiles is IConfigurableInstaller configurable)
            {
                actions.Add(new ConfigurationAction<TEventHandler>(configurable,
                    () => CurrentState.GetValueOrDefault(bootfiles.PackageName), actionFactory));
            }
            foreach (var action in actions)
            {
                ExecuteAction(action);
            }
        }];
    }

    protected virtual IPackageInstaller CreateBootfilesInstaller(IPackageInstaller? bootfilesPackageInstaller,
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
