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
        var batches = context.ReconciliationBatches(packages, installDir, backupStrategyProvider, eventHandler);

        if (batches.Count == 0)
        {
            eventHandler.ReconciliationNoPackages();
            return;
        }

        eventHandler.ReconciliationStart();
        try
        {
            Execute(batches, context, eventHandler, cancellationToken);
        }
        finally
        {
            afterInstall(context.CurrentState);
        }
        eventHandler.ReconciliationEnd();
    }

    private static void Execute(IReadOnlyCollection<IReconciliationBatch> batches,
        IReconciliationContext<TEventHandler> context,
        TEventHandler eventHandler,
        CancellationToken cancellationToken)
    {
        var progress = new PercentOfTotal(batches.Count);
        foreach (var batch in batches)
        {
            foreach (var action in batch.Resolve())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                context.ExecuteAction(action);
            }

            eventHandler.ProgressUpdate(progress.IncrementDone());
        }
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
