using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Packages.Installation;

public class PackageReconciliationService<TEventHandler>(
    IBackupStrategyProvider<DateTimeOffset, TEventHandler> backupStrategyProvider)
    : IReconciliationService<TEventHandler>
    where TEventHandler : PackageReconciliationService.IEventHandler
{
    public void Reconcile(
        IReadOnlyDictionary<string, PackageInstallationState> previousState,
        IEnumerable<IPackage> packages,
        string installDir,
        Action<IReadOnlyDictionary<string, PackageInstallationState>> afterInstall,
        TEventHandler eventHandler,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, PackageInstallationState> currentState = previousState;
        try
        {
            var context = CreateReconciliationContext(previousState,
                packages.Select(p => p.CreateInstaller()), installDir, eventHandler);
            currentState = context.CurrentState;
            Execute(context, eventHandler, cancellationToken);
        }
        finally
        {
            afterInstall(currentState);
        }
    }

    protected virtual ReconciliationContext CreateReconciliationContext(
        IReadOnlyDictionary<string, PackageInstallationState> previousState,
        IEnumerable<IPackageInstaller> installers, string installDir, TEventHandler eventHandler) =>
        new(previousState, installers, installDir, backupStrategyProvider, eventHandler);

    /// <summary>
    /// Called immediately before processing a package. CurrentState includes results and
    /// dependencies of earlier packages; PreviousState retains historical shadowing.
    /// </summary>
    protected virtual IReadOnlyCollection<IReconciliationAction> ReconciliationActions(
        string packageName, IPackageInstaller? installer, ReconciliationContext context)
    {
        var state = context.PreviousState.GetValueOrDefault(packageName);
        var actions = new List<IReconciliationAction>();
        var uninstall = state is not null && (
            state.Partial ||
            installer is null ||
            state.VersionHash is null ||
            installer.PackageVersionHash is null ||
            state.VersionHash != installer.PackageVersionHash ||
            state.ShadowedBy.Intersect(context.ToUninstall).Any() ||
            (state.ShadowedBy.Count > 0 && !state.ShadowedBy.Any(context.Processed.Contains)));

        if (uninstall)
        {
            actions.Add(context.Uninstall(packageName, state!));
        }
        if (installer is not null)
        {
            actions.Add(state is null || uninstall
                ? context.Install(installer)
                : context.Keep(packageName, state));
        }
        return actions;
    }

    protected sealed class ReconciliationContext
    {
        private readonly Dictionary<string, PackageInstallationState> currentState;
        private readonly ReconciliationAction<TEventHandler>.Factory actionFactory;
        private readonly string installDir;
        private readonly HashSet<string> processed = [];
        private readonly HashSet<string> toUninstall = [];

        public IReadOnlyDictionary<string, PackageInstallationState> PreviousState { get; }
        public IReadOnlyDictionary<string, PackageInstallationState> CurrentState { get; }
        public IReadOnlySet<string> Processed => processed;
        public IReadOnlySet<string> ToUninstall => toUninstall;

        internal readonly Dictionary<RootedPath, string> ClaimedFiles = [];
        internal readonly IReadOnlyCollection<(string Name, IPackageInstaller? Installer)> Packages;

        internal ReconciliationContext(
            IReadOnlyDictionary<string, PackageInstallationState> previousState,
            IEnumerable<IPackageInstaller> installers, string installDir,
            IBackupStrategyProvider<DateTimeOffset, TEventHandler> backupStrategyProvider,
            TEventHandler eventHandler)
        {
            PreviousState = previousState;
            currentState = new(previousState);
            CurrentState = new ReadOnlyDictionary<string, PackageInstallationState>(currentState);
            this.installDir = installDir;

            var joined = new OrderedDictionary<string, IPackageInstaller?>();
            var available = installers.ToArray();
            var names = available.Select(i => i.PackageName).ToImmutableHashSet();
            foreach (var name in previousState.Keys.Where(name => !names.Contains(name)))
            {
                joined.Add(name, null);
            }
            foreach (var installer in available)
            {
                joined.Add(installer.PackageName, installer);
            }
            Packages = joined.Select(p => (p.Key, p.Value)).ToArray();

            var previouslyInstalledPaths = previousState.Values
                .SelectMany(state => state.Files).Select(path => new RootedPath(installDir, path));
            var trackingBackupProvider = new SkipInstalledBackupStrategy.Provider<TEventHandler>(
                backupStrategyProvider, previouslyInstalledPaths);
            actionFactory = new(installDir, trackingBackupProvider, eventHandler);
        }

        public IReconciliationAction Install(IPackageInstaller installer) => actionFactory.Install(installer);

        public IReconciliationAction Uninstall(string packageName, PackageInstallationState state)
        {
            toUninstall.Add(packageName);
            return actionFactory.Uninstall(packageName, state);
        }

        public IReconciliationAction Keep(string packageName, PackageInstallationState state) =>
            actionFactory.Keep(packageName, state);

        internal void BeginPackage(string packageName) => processed.Add(packageName);

        internal void UpdateState(IReconciliationAction action, IReadOnlyCollection<string> shadowedBy)
        {
            var files = action.InstalledFiles;
            if (files.Count == 0)
            {
                currentState.Remove(action.PackageName);
            }
            else
            {
                currentState[action.PackageName] = new(
                    Time: action.InstallTime,
                    VersionHash: action.PackageVersionHash,
                    Partial: action.InstallState == IInstallation.State.PartiallyInstalled,
                    Dependencies: action.PackageDependencies,
                    ShadowedBy: shadowedBy,
                    Files: files);
            }

            // A previous owner may never reach its action if this run is interrupted.
            // Record transferred ownership now, including partially installed files.
            foreach (var (name, state) in currentState.ToArray())
            {
                var transferred = state.Files
                    .Select(path => (Path: path, Owner: ClaimedFiles.GetValueOrDefault(new(installDir, path))))
                    .Where(p => p.Owner is not null && p.Owner != name).ToArray();
                if (transferred.Length == 0)
                {
                    continue;
                }
                var remaining = state.Files.Except(transferred.Select(p => p.Path)).ToImmutableHashSet();
                if (remaining.Count == 0)
                {
                    currentState.Remove(name);
                }
                else
                {
                    currentState[name] = state with
                    {
                        Files = remaining,
                        ShadowedBy = state.ShadowedBy.Concat(transferred.Select(p => p.Owner!)).ToImmutableHashSet()
                    };
                }
            }
        }
    }

    private void Execute(ReconciliationContext context, TEventHandler eventHandler,
        CancellationToken cancellationToken)
    {
        if (context.Packages.Count == 0)
        {
            eventHandler.ReconciliationNoPackages();
            return;
        }

        eventHandler.ReconciliationStart();
        var progress = new PercentOfTotal(context.Packages.Count);
        foreach (var (packageName, installer) in context.Packages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            context.BeginPackage(packageName);
            var actions = ReconciliationActions(packageName, installer, context);
            foreach (var action in actions)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    eventHandler.ReconciliationEnd();
                    return;
                }
                ExecuteAction(action, context);
            }
            eventHandler.ProgressUpdate(progress.IncrementDone());
        }
        eventHandler.ReconciliationEnd();
    }

    private static void ExecuteAction(IReconciliationAction action, ReconciliationContext context)
    {
        var shadowedBy = new HashSet<string>(action.ShadowedBy);
        var callbacks = new ProcessingCallbacks<RootedPath>
        {
            Accept = gamePath =>
            {
                var owner = context.ClaimedFiles.GetValueOrDefault(gamePath);
                if (owner is null)
                {
                    return true;
                }
                if (owner != action.PackageName)
                {
                    shadowedBy.Add(owner);
                }
                return false;
            },
            Before = gamePath => context.ClaimedFiles.Add(gamePath, action.PackageName)
        };
        try
        {
            action.Execute(callbacks);
        }
        finally
        {
            context.UpdateState(action, shadowedBy);
        }
    }
}

public static class PackageReconciliationService
{
    public interface IEventHandler : IProgress, IBackupEventHandler
    {
        void ReconciliationNoPackages();
        void ReconciliationStart();
        void InstallingPackage(string packageName);
        void SkippingPackage(string packageName);
        void UninstallingPackage(string packageName);
        void ReconciliationEnd();
    }

    public interface IProgress
    {
        public void ProgressUpdate(IPercent? progress);
    }
}
