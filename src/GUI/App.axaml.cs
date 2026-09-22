using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Core.API;
using Core.SoftwareUpdates;

namespace AMS2CM.GUI;

public partial class App : Application
{
    private MainWindow? window;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            Dispatcher.UIThread.UnhandledException += HandleUnhandledException;
            desktop.Exit += (_, _) =>
                Dispatcher.UIThread.UnhandledException -= HandleUnhandledException;

            try
            {
                var config = Config.Load(desktop.Args ?? []);
                window = new MainWindow(CreateModManager(config), new GitHubUpdateChecker(config.Updates));
                desktop.MainWindow = window;
            }
            catch (Exception ex)
            {
                desktop.MainWindow = new ErrorDialog(ex);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void HandleUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
    {
        if (window is null || !window.IsVisible)
        {
            return;
        }

        args.Handled = true;
        window.SignalErrorAsync(args.Exception);
    }

    private static IModManager CreateModManager(Config config)
    {
        try
        {
            return Init.CreateModManager(config);
        }
        catch (Exception ex)
        {
            return new ThrowingModManager(ex);
        }
    }

    private class ThrowingModManager : IModManager
    {
        private readonly Exception ex;

        public ThrowingModManager(Exception ex)
        {
            this.ex = ex;
        }

        public string DisableMod(string packagePath) => throw ex;
        public string EnableMod(string packagePath) => throw ex;
        public ModState AddNewMod(string packagePath) => throw ex;
        public void DeleteMod(string packagePath) => throw ex;
        public List<ModState> FetchState() => throw ex;
        public void InstallEnabledMods(IEventHandler eventHandler, CancellationToken cancellationToken) => throw ex;
        public void UninstallAllMods(IEventHandler eventHandler, CancellationToken cancellationToken) => throw ex;
    }
}
