using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Core.Utils;

namespace Core.Packages.Installation;

internal sealed class ReconciliationState
{
    private readonly string installDir;
    private readonly Dictionary<string, PackageInstallationState> packages;
    private readonly Dictionary<RootedPath, HashSet<string>> owners = [];
    private readonly Dictionary<RootedPath, string> claimedFiles = [];
    private readonly HashSet<string> affectedPackages = [];

    public IReadOnlyDictionary<string, PackageInstallationState> Packages { get; }

    public ReconciliationState(string installDir, IReadOnlyDictionary<string, PackageInstallationState> previousState)
    {
        this.installDir = installDir;
        packages = new(previousState);
        Packages = new ReadOnlyDictionary<string, PackageInstallationState>(packages);
        foreach (var (name, state) in previousState)
        {
            IndexFiles(name, state.Files);
        }
    }

    public string? ClaimedBy(RootedPath path) => claimedFiles.GetValueOrDefault(path);

    public void InvalidateShadowedBy(string packageName)
    {
        // Persist the pending rebuild before uninstalling can release shadowed paths.
        foreach (var (name, installed) in packages.Where(p => p.Value.ShadowedBy.Contains(packageName)).ToArray())
        {
            packages[name] = installed with { Partial = true };
        }
    }

    public void Claim(RootedPath path, string packageName)
    {
        claimedFiles.Add(path, packageName);
        if (owners.TryGetValue(path, out var previousOwners))
        {
            affectedPackages.UnionWith(previousOwners);
        }
    }

    public void Update(string packageName, PackageInstallationState? state)
    {
        SetPackage(packageName, state);
        affectedPackages.Add(packageName);

        // Persist attempted ownership transfers even if the previous owner never runs.
        foreach (var name in affectedPackages)
        {
            if (!packages.TryGetValue(name, out var installed)) continue;

            var transferred = installed.Files
                .Select(path => (Path: path, Owner: ClaimedBy(new(installDir, path))))
                .Where(p => p.Owner is not null && p.Owner != name).ToArray();
            if (transferred.Length == 0) continue;

            SetPackage(name, installed with
            {
                // Derived configuration and dependencies must reflect the surviving files.
                Partial = true,
                Files = installed.Files.Except(transferred.Select(p => p.Path)).ToImmutableHashSet(),
                RemovedFiles = installed.RemovedFiles.Except(transferred.Select(p => p.Path), StringComparer.OrdinalIgnoreCase)
                    .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
                FileStates = installed.FileStates.Where(file => !transferred.Any(p => p.Path == file.Key))
                    .ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
                ShadowedBy = installed.ShadowedBy.Concat(transferred.Select(p => p.Owner!)).ToImmutableHashSet()
            });
        }
        affectedPackages.Clear();
    }

    private void SetPackage(string packageName, PackageInstallationState? state)
    {
        if (packages.Remove(packageName, out var previous))
        {
            foreach (var path in previous.Files.Select(path => new RootedPath(installDir, path)).Distinct())
            {
                var previousOwners = owners[path];
                previousOwners.Remove(packageName);
                if (previousOwners.Count == 0) owners.Remove(path);
            }
        }

        if (state is null || state.Files.Count == 0) return;

        packages[packageName] = state;
        IndexFiles(packageName, state.Files);
    }

    private void IndexFiles(string packageName, IEnumerable<string> files)
    {
        foreach (var path in files.Select(path => new RootedPath(installDir, path)))
        {
            if (!owners.TryGetValue(path, out var currentOwners))
            {
                owners[path] = currentOwners = [];
            }
            currentOwners.Add(packageName);
        }
    }
}
