using Uno.UI.Hosting;

namespace AMS2CM.GUI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseWin32()
            .Build();

        host.Run();
    }
}
