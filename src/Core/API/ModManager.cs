namespace Core.API;

internal sealed class ModManager(
    IModStateReader stateReader,
    IModPackageOperations packageOperations,
    IModSynchronizationService synchronizationService) : IModManager
{
    public List<ModState> FetchState() => stateReader.FetchState();
    public void AddNewMod(string packagePath) => packageOperations.AddNewMod(packagePath);
    public void DeleteMod(string packagePath) => packageOperations.DeleteMod(packagePath);
    public string EnableMod(string packagePath) => packageOperations.EnableMod(packagePath);
    public string DisableMod(string packagePath) => packageOperations.DisableMod(packagePath);

    public void InstallEnabledMods(IEventHandler eventHandler, CancellationToken cancellationToken = default) =>
        synchronizationService.InstallEnabledMods(eventHandler, cancellationToken);

    public void UninstallAllMods(IEventHandler eventHandler, CancellationToken cancellationToken = default) =>
        synchronizationService.UninstallAllMods(eventHandler, cancellationToken);
}
