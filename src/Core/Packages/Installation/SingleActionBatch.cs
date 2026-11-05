namespace Core.Packages.Installation;

internal sealed class SingleActionBatch(IReconciliationAction action) : IReconciliationBatch
{
    public IReadOnlyCollection<IReconciliationAction> Resolve() => [action];
}
