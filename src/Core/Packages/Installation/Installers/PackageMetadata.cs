using System.Collections.Immutable;

namespace Core.Packages.Installation.Installers;

public sealed record PackageMetadata(string Name, int? VersionHash, IReadOnlySet<string> Dependencies)
{
    public PackageMetadata(string name, int? versionHash)
        : this(name, versionHash, ImmutableHashSet<string>.Empty)
    {
    }
}
