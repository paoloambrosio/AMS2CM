namespace Core.Mods.Installation;

public sealed record InstallationPaths(string GameDirectory, string ModsDirectory, string TempDirectory)
{
    internal const string ModsDirectoryName = "Mods";
}
