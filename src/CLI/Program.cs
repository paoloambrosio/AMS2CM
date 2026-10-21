using System.IO.Abstractions;
using AMS2CM.CLI;
using Core.API;

try
{
    var fileSystem = new FileSystem();
    var config = Config.Load(fileSystem.FileInfo.New(Path.Combine(AppContext.BaseDirectory, "Config.yaml")), args);
    var modManager = Init.CreateModManager(fileSystem, config);
    modManager.InstallEnabledMods(new ConsoleEventLogger());
}
catch (Exception e)
{
    Console.WriteLine($"Error: {e.Message}");
}

Console.WriteLine("Press any key to exit.");
Console.ReadKey();
