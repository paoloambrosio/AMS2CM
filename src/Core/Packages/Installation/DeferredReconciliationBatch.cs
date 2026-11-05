namespace Core.Packages.Installation;

internal sealed class DeferredReconciliationBatch(Func<IReadOnlyCollection<IReconciliationAction>> resolve)
    : IReconciliationBatch
{
    public IReadOnlyCollection<IReconciliationAction> Resolve() => resolve();
}
