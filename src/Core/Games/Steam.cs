using Core.Utils;
using Gameloop.Vdf;
using Gameloop.Vdf.JsonConverter;
using Microsoft.Win32;
using System.IO.Abstractions;

namespace Core.Games;

public class Steam(IFileSystem fileSystem) : ISteamLibraryLocator
{
    private static readonly string[] RegistryPaths = {
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
    };

    public string MainInstallationPath()
    {
        try
        {
            return RegistryPaths
                .SelectNotNull(registryPath =>
                    Registry.GetValue(registryPath, "InstallPath", null) as string
                ).Distinct().Single(path => fileSystem.Directory.Exists(path));
        }
        catch (Exception e)
        {
            throw new Exception("Cannot find Steam installation path", e);
        }
    }

    public string AppLibraryPath(string appId)
    {
        var steamDir = MainInstallationPath();
        var libraryFoldersPath = Path.Combine(steamDir, "steamapps", "libraryfolders.vdf");
        var gameLibraryFolders = ReadLibraryFolders(libraryFoldersPath)
            .Where(lf => lf.Apps.ContainsKey(appId)).ToArray();
        return gameLibraryFolders.Length switch
        {
            0 => throw new Exception($"Cannot find app ID {appId} in any Steam library"),
            1 => gameLibraryFolders.Single().Path,
            var count => throw new Exception($"App ID {appId} found in {count} Steam libraries")
        };
    }

    private IEnumerable<LibraryFolder> ReadLibraryFolders(string path)
    {
        var libraryFolders = fileSystem.File.ReadAllText(path);
        var vdfRoot = VdfConvert.Deserialize(libraryFolders);
        return vdfRoot.ToJson().Value
            .SelectMany(_ => _)
            .Select(_ => _.ToObject<LibraryFolder>()!);
    }

    private record LibraryFolder
    (
        string Path,
        Dictionary<string, string> Apps
    );
}
