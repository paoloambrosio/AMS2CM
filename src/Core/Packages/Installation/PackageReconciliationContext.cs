using System.Collections.Immutable;
using System.IO.Abstractions;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Packages.Installation;

public class PackageReconciliationContext<TEventHandler>(
    IReadOnlyDictionary<string, PackageInstallationState> currentState) : IReconciliationContext<TEventHandler>
    where TEventHandler : IReconciliationContext.IEventHandler
{
    public static IReconciliationContext<TEventHandler>.FactoryFunc Factory() =>
        currentState => new PackageReconciliationContext<TEventHandler>(currentState);

    private readonly Dictionary<string, PackageInstallationState> currentState = new(currentState);
    private readonly Dictionary<RootedPath, string> installedFiles = [];
    protected readonly HashSet<string> ReplacedPackages = [];

    public IReadOnlyDictionary<string, PackageInstallationState> CurrentState => currentState;

    protected sealed record PlanningInputs(
        IReadOnlyList<IPackageInstaller> Installers,
        IReadOnlyDictionary<string, PackageInstallationState> PlanningState);

    private protected sealed record PackageAction(string PackageName, Action Execute);

    public IReadOnlyCollection<Action> ReconciliationActions(IEnumerable<IPackage> packages,
        IDirectoryInfo installDir,
        IBackupStrategyProvider<DateTimeOffset, TEventHandler> backupStrategyProvider,
        TEventHandler eventHandler)
    {
        var inputs = CreatePlanningInputs(packages, eventHandler);
        var actionFactory = CreateActionFactory(backupStrategyProvider, installDir, eventHandler);
        return CreateActions(inputs, actionFactory);
    }

    private protected virtual IReadOnlyCollection<Action> CreateActions(
        PlanningInputs inputs, ReconciliationAction<TEventHandler>.Factory actionFactory) =>
        CreatePackageActions(inputs, actionFactory).Select(action => action.Execute).ToArray();

    private protected IReadOnlyCollection<PackageAction> CreatePackageActions(
        PlanningInputs inputs, ReconciliationAction<TEventHandler>.Factory actionFactory)
    {
        var joinedState = JoinPreviousState(inputs.PlanningState, inputs.Installers);
        HashSet<string> processed = [];
        HashSet<string> toUninstall = [];
        List<PackageAction> actions = [];
        foreach (var (packageName, (state, installer)) in joinedState)
        {
            processed.Add(packageName);
            var uninstall = state is not null && ShouldUninstall(state, installer, toUninstall, processed);
            if (uninstall)
            {
                toUninstall.Add(packageName);
            }

            // Decide replacements from the original plan, but resolve ownership after earlier actions.
            actions.Add(new PackageAction(packageName, () =>
            {
                foreach (var action in ResolveActions(packageName, state, installer, uninstall, actionFactory))
                {
                    ExecuteAction(action);
                }
            }));
        }
        return actions;
    }

    protected virtual PlanningInputs CreatePlanningInputs(IEnumerable<IPackage> packages,
        TEventHandler eventHandler) => new(packages.Select(p => p.CreateInstaller()).ToArray(), currentState);

    private static OrderedDictionary<string, (PackageInstallationState?, IPackageInstaller?)> JoinPreviousState(
        IReadOnlyDictionary<string, PackageInstallationState> planningState,
        IReadOnlyCollection<IPackageInstaller> installersCollection)
    {
        var joinedState = new OrderedDictionary<string, (PackageInstallationState?, IPackageInstaller?)>();
        var available = installersCollection.Select(p => p.PackageName).ToImmutableHashSet();
        foreach (var (packageName, state) in planningState)
        {
            if (!available.Contains(packageName))
            {
                joinedState.Add(packageName, (state, null));
            }
        }

        foreach (var i in installersCollection)
        {
            joinedState.Add(i.PackageName, (planningState.GetValueOrDefault(i.PackageName), i));
        }

        return joinedState;
    }

    private ReconciliationAction<TEventHandler>.Factory CreateActionFactory(
        IBackupStrategyProvider<DateTimeOffset, TEventHandler> backupStrategyProvider, IDirectoryInfo installDir,
        TEventHandler eventHandler)
    {
        var installRoot = new PathRoot(installDir);
        var previouslyInstalledPaths = currentState.Values
            .SelectMany(state => state.Files).Select(installRoot.Combine);
        var trackingBackupProvider = new SkipInstalledBackupStrategy.Provider<TEventHandler>(
            backupStrategyProvider, previouslyInstalledPaths);
        return new ReconciliationAction<TEventHandler>.Factory(installDir, trackingBackupProvider, eventHandler);
    }

    private List<IReconciliationAction> ResolveActions(
        string packageName, PackageInstallationState? state, IPackageInstaller? installer, bool uninstall,
        ReconciliationAction<TEventHandler>.Factory actionFactory)
    {
        if (state is not null)
        {
            var ownership = currentState[packageName];
            // Keep planning metadata, including remapped bootfiles dependencies.
            state = state with { Files = ownership.Files, ShadowedBy = ownership.ShadowedBy };
        }
        List<IReconciliationAction> actions = [];
        if (uninstall && state is not null)
        {
            actions.Add(actionFactory.Uninstall(packageName, state));
        }
        if (installer is null) return actions;
        if (state is null || uninstall)
        {
            actions.Add(actionFactory.Install(installer));
        }
        else
        {
            actions.Add(actionFactory.Keep(packageName, state));
        }
        return actions;
    }

    private static bool ShouldUninstall(PackageInstallationState state, IPackageInstaller? installer,
        IReadOnlySet<string> toUninstall, IReadOnlySet<string> processed) =>
        state.Partial ||
        installer is null ||
        state.VersionHash is null ||
        installer.PackageVersionHash is null ||
        state.VersionHash != installer.PackageVersionHash ||
        state.ShadowedBy.Intersect(toUninstall).Any() ||
        (state.ShadowedBy.Count > 0 && !state.ShadowedBy.Any(processed.Contains));

    protected void ExecuteAction(IReconciliationAction action, bool allowUpdatingOwnedFiles = false)
    {
        if (action.InstallState == IInstallation.State.NotInstalled)
        {
            ReplacedPackages.Add(action.PackageName);
        }
        var shadowedBy = new HashSet<string>(action.ShadowedBy);
        var installCallbacks = new ProcessingCallbacks<RootedPath>
        {
            Accept = gamePath =>
            {
                var shadowingPackageName = installedFiles.GetValueOrDefault(gamePath);
                if (shadowingPackageName is null ||
                    (shadowingPackageName == action.PackageName && allowUpdatingOwnedFiles))
                {
                    return true;
                }

                if (shadowingPackageName != action.PackageName)
                {
                    shadowedBy.Add(shadowingPackageName);
                }

                return false;
            },
            Before = gamePath =>
            {
                installedFiles[gamePath] = action.PackageName;
                foreach (var (name, state) in currentState.ToArray())
                {
                    if (name != action.PackageName && state.Files.Any(p => gamePath.Root.Combine(p) == gamePath))
                    {
                        currentState[name] = state with
                        {
                            Files = state.Files.Where(p => gamePath.Root.Combine(p) != gamePath).ToArray(),
                            ShadowedBy = state.ShadowedBy.Append(action.PackageName).Distinct().ToArray()
                        };
                    }
                }
            }
        };
        try
        {
            action.Execute(installCallbacks);
        }
        finally
        {
            if (action.InstallState == IInstallation.State.NotInstalled)
            {
                ReplacedPackages.Add(action.PackageName);
            }
            UpdateState(action, shadowedBy);
        }
    }

    private void UpdateState(IReconciliationAction action, IReadOnlyCollection<string> shadowedBy)
    {
        var files = action.InstalledFiles;
        var packageName = action.PackageName;
        foreach (var path in installedFiles.Where(p => p.Value == packageName &&
                     !files.Contains(p.Key.Relative, StringComparer.OrdinalIgnoreCase))
                     .Select(p => p.Key).ToArray())
        {
            installedFiles.Remove(path);
        }
        if (files.Count == 0)
        {
            currentState.Remove(packageName);
        }
        else
        {
            currentState[packageName] = new PackageInstallationState(
                Time: action.InstallTime,
                VersionHash: action.PackageVersionHash,
                Partial: action.InstallState == IInstallation.State.PartiallyInstalled,
                Dependencies: action.PackageDependencies,
                ShadowedBy: shadowedBy,
                Files: files
            );
        }
    }
}
