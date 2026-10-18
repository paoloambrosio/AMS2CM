namespace Core.Packages.Installation;

public record PackageInstallationState(
    DateTimeOffset Time,
    // Unknown when partially installed or upgrading from a previous version
    int? VersionHash,
    bool Partial,
    IReadOnlyCollection<string> Dependencies,
    IReadOnlyCollection<string> Files,
    IReadOnlyCollection<string> ShadowedBy
)
{
    // Removal markers own a path while requiring the destination to stay absent.
    public IReadOnlyCollection<string> RemovedFiles { get; init; } = [];
    public IReadOnlyDictionary<string, InstalledFileState> FileStates { get; init; } =
        new Dictionary<string, InstalledFileState>();
}
