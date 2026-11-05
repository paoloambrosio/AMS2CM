namespace Core.Packages.Installation;

/// <summary>
/// A unit of reconciliation progress whose actions are resolved when execution reaches it.
/// </summary>
public interface IReconciliationBatch
{
    IReadOnlyCollection<IReconciliationAction> Resolve();
}
