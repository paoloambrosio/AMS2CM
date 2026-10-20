using Core.Mods;
using Core.Packages;
using Core.Packages.Installation;
using Core.Packages.Repository;
using Core.State;
using Core.Utils;

namespace Core.API;

internal interface IModStateReader
{
    List<ModState> FetchState();
}

internal sealed class ModStateReader(
    IPackageRepository packageRepository,
    IStatePersistence statePersistence,
    IBootfilesNaming bootfilesNaming) : IModStateReader
{
    public List<ModState> FetchState()
    {
        var installedMods = statePersistence.ReadState().Installation;
        var enabledModPackages = packageRepository.ListEnabled().ToDictionary(p => p.Name);
        var disabledModPackages = packageRepository.ListDisabled().ToDictionary(p => p.Name);
        var availableModPackages = enabledModPackages.Merge(disabledModPackages);

        var isModInstalled = DependencyResolver
            .CollectValues(
                installedMods,
                s => s.Dependencies.Concat(s.ShadowedBy).ToArray(),
                s => s?.Partial ?? true)
            .SelectValues<string, IReadOnlySet<bool>, bool?>(
                partials => partials.Any(p => p) ? null : true);

        var modsOutOfDate = installedMods.SelectValues((packageName, modInstallationState) =>
        {
            availableModPackages.TryGetValue(packageName, out var modPackage);
            return IsOutOfDate(modPackage, modInstallationState);
        });

        var allPackageNames = installedMods.Keys.Where(packageName => !bootfilesNaming.IsBootfiles(packageName))
            .Concat(enabledModPackages.Keys)
            .Concat(disabledModPackages.Keys)
            .Distinct();

        return allPackageNames
            .Select(packageName => new ModState(
                PackageName: packageName,
                PackageLocation: availableModPackages.TryGetValue(packageName, out var modPackage) ? modPackage.Location : null,
                IsInstalled: isModInstalled.GetValueOrDefault(packageName, false),
                IsEnabled: enabledModPackages.ContainsKey(packageName),
                IsOutOfDate: modsOutOfDate.TryGetValue(packageName, out var isOutOfDate) && isOutOfDate
            )).ToList();
    }

    private static bool IsOutOfDate(IPackage? modPackage, PackageInstallationState? modInstallationState)
    {
        if (modPackage is null || modInstallationState is null)
        {
            return false;
        }
        var installedVersionHash = modInstallationState.VersionHash;
        // A missing version represents a partial installation or legacy state.
        return installedVersionHash is null || installedVersionHash != modPackage.VersionHash;
    }
}
