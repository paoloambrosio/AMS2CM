using Uno.UI.Hosting;

namespace AMS2CM.GUI.Platforms.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseWin32()
            .Build();
        host.Run();
    }
}
