using System.IO.Abstractions;
using Core.Packages.Installation.Backup;
using Core.Utils;

namespace Core.Packages.Installation;

public sealed class ReconciliationService<TEventHandler>(
    IBackupStrategyProvider<DateTimeOffset, TEventHandler> backupStrategyProvider,
    IReconciliationContext<TEventHandler>.FactoryFunc createContext)
    : IReconciliationService<TEventHandler>
    where TEventHandler : ReconciliationService.IEventHandler
{
    public void Reconcile(IReadOnlyDictionary<string, PackageInstallationState> previousState,
        IEnumerable<IPackage> packages,
        IDirectoryInfo installDir,
        Action<IReadOnlyDictionary<string, PackageInstallationState>> afterInstall,
        TEventHandler eventHandler,
        CancellationToken cancellationToken)
    {
        var context = createContext(previousState);
        var actions = context.ReconciliationActions(packages, installDir, backupStrategyProvider, eventHandler);
        try
        {
            Execute(actions, context, eventHandler, cancellationToken);
        }
        finally
        {
            afterInstall(context.CurrentState);
        }
    }

    private static void Execute(IReadOnlyCollection<Func<IReadOnlyCollection<IReconciliationAction>>> reconciliationActions,
        IReconciliationContext<TEventHandler> context,
        TEventHandler eventHandler,
        CancellationToken cancellationToken)
    {
        if (reconciliationActions.Count == 0)
        {
            eventHandler.ReconciliationNoPackages();
            return;
        }

        eventHandler.ReconciliationStart();

        var progress = new PercentOfTotal(reconciliationActions.Count);
        foreach (var resolve in reconciliationActions.TakeWhile(_ => !cancellationToken.IsCancellationRequested))
        {
            foreach (var action in resolve())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    eventHandler.ReconciliationEnd();
                    return;
                }

                context.ExecuteAction(action);
            }
            eventHandler.ProgressUpdate(progress.IncrementDone());
        }

        eventHandler.ReconciliationEnd();
    }
}

public static class ReconciliationService
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
