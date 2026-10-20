using AMS2CM.CLI;
using Core;
using Core.API;
using Microsoft.Extensions.DependencyInjection;

try
{
    var config = Config.Load(args);
    var services = new ServiceCollection().AddCoreServices(config);
    services.AddSingleton<ConsoleEventLogger>();
    using var provider = services.BuildServiceProvider(new ServiceProviderOptions
    {
        ValidateOnBuild = true,
        ValidateScopes = true
    });
    provider.GetRequiredService<IModManager>().InstallEnabledMods(provider.GetRequiredService<ConsoleEventLogger>());
}
catch (Exception e)
{
    Console.WriteLine($"Error: {e.Message}");
}

Console.WriteLine("Press any key to exit.");
Console.ReadKey();
