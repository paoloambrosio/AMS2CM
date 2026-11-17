using System.Runtime.Versioning;

namespace Core.Games;

[SupportedOSPlatform("linux")]
public class LinuxSteam : ISteamLibraryLocator
{
    public string AppLibraryPath(string appId) =>
        throw new NotImplementedException(
            "Steam library discovery is not implemented on Linux. Specify an absolute Game:Path.");
}
