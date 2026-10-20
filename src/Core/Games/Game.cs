using System.Diagnostics;
using System.IO.Abstractions;

namespace Core.Games;

public class Game : IGame
{
    public interface IConfig
    {
        string SteamId
        {
            get;
        }

        string Path
        {
            get;
        }

        string ProcessName
        {
            get;
        }
    }

    private readonly IConfig config;

    public Game(IConfig config, IFileSystem fileSystem, ISteamLibraryLocator steamLibraryLocator)
    {
        var installationDirectory = Path.IsPathFullyQualified(config.Path)
            ? config.Path
            : Path.Combine(steamLibraryLocator.AppLibraryPath(config.SteamId), config.Path);
        if (!fileSystem.Directory.Exists(installationDirectory))
        {
            throw new Exception("The game directory does not exist");
        }

        this.config = config;
        InstallationDirectory = installationDirectory;
    }

    public string InstallationDirectory
    {
        get;
    }

    public bool IsRunning => Process.GetProcesses().Any(_ => _.ProcessName == config.ProcessName);

}
