using Core.Games;
using Core.Packages;
using Core.Packages.Installation;
using Core.Packages.Repository;
using Core.State;
using Core.Utils;

namespace Core.API;

internal interface IModSynchronizationService
{
    void InstallEnabledMods(IEventHandler eventHandler, CancellationToken cancellationToken);
    void UninstallAllMods(IEventHandler eventHandler, CancellationToken cancellationToken);
}

internal sealed class ModSynchronizationService(
    IGame game,
    IPackageRepository packageRepository,
    IReconciliationService<IEventHandler> reconciliationService,
    IStatePersistence statePersistence,
    ITempDir tempDir) : IModSynchronizationService
{
    public void InstallEnabledMods(IEventHandler eventHandler, CancellationToken cancellationToken)
    {
        CheckGameNotRunning();
        // Some systems load oo2core even when pak files do not use Mermaid or Kraken.
        AddToEnvironmentPath(game.InstallationDirectory);
        tempDir.Cleanup();
        UpdateMods(packageRepository.ListEnabled().Reverse(), eventHandler, cancellationToken);
        tempDir.Cleanup();
    }

    public void UninstallAllMods(IEventHandler eventHandler, CancellationToken cancellationToken)
    {
        CheckGameNotRunning();
        UpdateMods(Array.Empty<IPackage>(), eventHandler, cancellationToken);
    }

    private void CheckGameNotRunning()
    {
        if (game.IsRunning)
        {
            throw new Exception("The game is running.");
        }
    }

    private static void AddToEnvironmentPath(string additionalPath)
    {
        const string pathEnvVar = "PATH";
        var env = Environment.GetEnvironmentVariable(pathEnvVar);
        if (env is not null && env.Contains(additionalPath))
        {
            return;
        }
        Environment.SetEnvironmentVariable(pathEnvVar, $"{env};{additionalPath}");
    }

    private void UpdateMods(IEnumerable<IPackage> packages, IEventHandler eventHandler, CancellationToken cancellationToken) =>
        reconciliationService.Reconcile(
            statePersistence.ReadState().Installation,
            packages,
            game.InstallationDirectory,
            nextState => statePersistence.WriteState(new SavedState(Installation: nextState)),
            eventHandler,
            cancellationToken);
}
