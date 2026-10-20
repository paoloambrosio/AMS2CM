using Core;
using Core.API;
using Core.SoftwareUpdates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace AMS2CM.GUI;

public partial class App : Application
{
    private MainWindow? window;
    private ServiceProvider? serviceProvider;

    public App()
    {
        InitializeComponent();
        UnhandledException += HandleUnhandledException;
    }

    private void HandleUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        if (window is null)
        {
            return;
        }
        window.SignalErrorAsync(args.Exception);
        args.Handled = true;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var config = Config.Load(Environment.GetCommandLineArgs());
        var services = new ServiceCollection().AddCoreServices(config);
        services.AddSingleton(provider =>
        {
            IModManager modManager;
            try
            {
                modManager = provider.GetRequiredService<IModManager>();
            }
            catch (Exception ex)
            {
                modManager = new ThrowingModManager(ex);
            }
            return new MainWindow(modManager, provider.GetRequiredService<IUpdateChecker>());
        });
        serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        try
        {
            window = serviceProvider.GetRequiredService<MainWindow>();
            window.Closed += HandleWindowClosed;
            window.Activate();
        }
        catch
        {
            DisposeServiceProvider();
            throw;
        }
    }

    private void HandleWindowClosed(object sender, WindowEventArgs args) => DisposeServiceProvider();

    private void DisposeServiceProvider() => Interlocked.Exchange(ref serviceProvider, null)?.Dispose();

    private class ThrowingModManager : IModManager
    {
        private readonly Exception ex;

        public ThrowingModManager(Exception ex)
        {
            this.ex = ex;
        }

        public string DisableMod(string packagePath) => throw ex;
        public string EnableMod(string packagePath) => throw ex;
        public void AddNewMod(string packagePath) => throw ex;
        public void DeleteMod(string packagePath) => throw ex;
        public List<ModState> FetchState() => throw ex;
        public void InstallEnabledMods(IEventHandler eventHandler, CancellationToken cancellationToken) => throw ex;
        public void UninstallAllMods(IEventHandler eventHandler, CancellationToken cancellationToken) => throw ex;
    }
}
