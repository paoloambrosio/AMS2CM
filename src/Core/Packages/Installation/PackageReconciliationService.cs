using System.Collections.Immutable;
using System.IO.Abstractions;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Packages.Installation;

public class PackageReconciliationService<TEventHandler>(
    IBackupStrategyProvider<DateTimeOffset, TEventHandler> backupStrategyProvider,
    IFileSystem? fileSystem = null)
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
        new(previousState, installers, installDir, backupStrategyProvider, eventHandler, fileSystem ?? new FileSystem());

    /// <summary>
    /// Called immediately before processing a package. CurrentState includes results and
    /// dependencies of earlier packages; PreviousState retains historical shadowing.
    /// </summary>
    protected virtual IReadOnlyCollection<IReconciliationAction> ReconciliationActions(
        string packageName, IPackageInstaller? installer, ReconciliationContext context)
    {
        var state = context.PreviousState.GetValueOrDefault(packageName);
        var actions = new List<IReconciliationAction>();
        var uninstall = state is not null &&
            (installer is null || NeedsReinstall(packageName, state, installer, context));

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

    protected virtual bool NeedsReinstall(string packageName, PackageInstallationState state,
        IPackageInstaller installer, ReconciliationContext context) =>
        state.Partial ||
        state.VersionHash is null ||
        installer.PackageVersionHash is null ||
        state.VersionHash != installer.PackageVersionHash ||
        context.FilesChanged(state) ||
        state.ShadowedBy.Any(context.ToUninstall.Contains) ||
        state.ShadowedBy.Any(name => !context.Processed.Contains(name));

    protected sealed class ReconciliationContext
    {
        private readonly ReconciliationState state;
        private readonly ReconciliationAction<TEventHandler>.Factory actionFactory;
        private readonly string installDir;
        private readonly IFileSystem fileSystem;
        private readonly HashSet<string> processed = [];
        private readonly HashSet<string> toUninstall = [];

        public IReadOnlyDictionary<string, PackageInstallationState> PreviousState { get; }
        public IReadOnlyDictionary<string, PackageInstallationState> CurrentState => state.Packages;
        public IReadOnlySet<string> Processed => processed;
        public IReadOnlySet<string> ToUninstall => toUninstall;

        internal readonly IReadOnlyCollection<(string Name, IPackageInstaller? Installer)> Packages;

        internal ReconciliationContext(
            IReadOnlyDictionary<string, PackageInstallationState> previousState,
            IEnumerable<IPackageInstaller> installers, string installDir,
            IBackupStrategyProvider<DateTimeOffset, TEventHandler> backupStrategyProvider,
            TEventHandler eventHandler, IFileSystem fileSystem)
        {
            PreviousState = previousState;
            state = new(installDir, previousState);
            this.installDir = installDir;
            this.fileSystem = fileSystem;

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
                .SelectMany(state => state.Files.Select(path => (new RootedPath(installDir, path), state.Time)));
            var trackingBackupProvider = new SkipInstalledBackupStrategy.Provider<TEventHandler>(
                backupStrategyProvider, previouslyInstalledPaths, fileSystem);
            actionFactory = new(installDir, trackingBackupProvider, eventHandler);
        }

        public IReconciliationAction Install(IPackageInstaller installer) => actionFactory.Install(installer);

        public IReconciliationAction Uninstall(string packageName, PackageInstallationState state)
        {
            toUninstall.Add(packageName);
            this.state.InvalidateShadowedBy(packageName);
            return actionFactory.Uninstall(packageName, state);
        }

        public IReconciliationAction Keep(string packageName, PackageInstallationState state) =>
            actionFactory.Keep(packageName, state);

        internal void BeginPackage(string packageName) => processed.Add(packageName);

        internal string InstallDir => installDir;

        internal IBackupStrategy BackupStrategy(DateTimeOffset time) =>
            actionFactory.BackupStrategyProvider.BackupStrategy(time, actionFactory.EventHandler);

        internal string? ClaimedBy(RootedPath path) => state.ClaimedBy(path);

        internal void Claim(RootedPath path, string packageName) => state.Claim(path, packageName);

        internal bool FilesChanged(PackageInstallationState previous)
        {
            var removed = previous.RemovedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return previous.Files.Any(relative =>
            {
                var path = new RootedPath(installDir, relative);
                // Higher-priority packages already established the intended contents.
                if (ClaimedBy(path) is not null) return false;
                var exists = fileSystem.File.Exists(path.Full);
                return removed.Contains(relative)
                    ? exists
                    : !exists || fileSystem.File.GetCreationTimeUtc(path.Full) > previous.Time.UtcDateTime;
            });
        }

        internal bool IsNewlyShadowed(string packageName, PackageInstallationState previous) =>
            previous.Files.Any(path => ClaimedBy(new(installDir, path)) is { } owner &&
                owner != packageName && !previous.ShadowedBy.Contains(owner));

        internal void UpdateState(IReconciliationAction action, IReadOnlyCollection<string> shadowedBy)
        {
            var files = action.InstalledFiles;
            state.Update(action.PackageName, files.Count == 0
                ? null
                : new PackageInstallationState(
                    Time: action.InstallTime,
                    VersionHash: action.PackageVersionHash,
                    Partial: action.InstallState == IInstallation.State.PartiallyInstalled,
                    Dependencies: action.PackageDependencies,
                    ShadowedBy: shadowedBy,
                    Files: files)
                {
                    RemovedFiles = files.Where(path => !fileSystem.File.Exists(Path.Combine(installDir, path)))
                        .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase)
                });
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
                var owner = context.ClaimedBy(gamePath);
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
            Before = gamePath => context.Claim(gamePath, action.PackageName)
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
