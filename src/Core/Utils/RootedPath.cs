namespace Core.Utils;

public sealed record RootedPath(string Root, string Relative = "")
{
    public string Full { get; } = Path.Combine(Root, Relative);
    private readonly string comparisonPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(Root, Relative)));

    public RootedPath SubPath(string subPath) =>
        new(Root, Path.Combine(Relative, subPath));

    /// <summary>
    /// Compares absolute paths ignoring case, redundant separators, and dot segments.
    /// </summary>
    public bool Equals(RootedPath? other) =>
        other is not null &&
        StringComparer.OrdinalIgnoreCase.Equals(comparisonPath, other.comparisonPath);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(comparisonPath);
}
