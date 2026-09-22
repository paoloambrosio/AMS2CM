using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Core.API;
using Core.SoftwareUpdates;
using Core.Utils;

namespace AMS2CM.GUI;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<ModVM> modList = new();
    private readonly IModManager modManager;
    private readonly IUpdateChecker updateChecker;
    private PointerPressedEventArgs? dragPress;
    private Point dragStart;
    private bool isDragging;
    private bool isShowingError;

    public MainWindow(IModManager modManager, IUpdateChecker updateChecker)
    {
        InitializeComponent();
        this.modManager = modManager;
        this.updateChecker = updateChecker;
        ModListView.ItemsSource = modList;
        ModListView.AddHandler(PointerPressedEvent, ModListView_PointerPressed, RoutingStrategies.Tunnel);
        ModListView.AddHandler(PointerMovedEvent, ModListView_PointerMoved, RoutingStrategies.Tunnel);
        ModListView.AddHandler(PointerReleasedEvent, (_, _) => dragPress = null,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private async void Window_Opened(object? sender, EventArgs e)
    {
        SyncModListView();
        NewVersionBlock.IsVisible = await updateChecker.CheckUpdateAvailable();
    }

    private async void NewVersionLink_Click(object? sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(
            new Uri("https://www.racedepartment.com/downloads/automobilista-2-content-manager.59727/"));
    }

    private async void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        await SyncDialog.ShowAsync(this, (dialog, cancellationToken) =>
        {
            var eventLogger = new SyncDialogEventLogger(dialog);
            modManager.InstallEnabledMods(eventLogger, cancellationToken);
            var status = cancellationToken.IsCancellationRequested ? "aborted" : "completed";
            dialog.LogMessage($"Synchronization {status}.");
        });
        SyncModListView();
    }

    private async void UninstallAllItem_Click(object? sender, RoutedEventArgs e)
    {
        await SyncDialog.ShowAsync(this, (dialog, cancellationToken) =>
        {
            var eventLogger = new SyncDialogEventLogger(dialog);
            modManager.UninstallAllMods(eventLogger, cancellationToken);
            dialog.SetProgress(1.0);
            var status = cancellationToken.IsCancellationRequested ? "aborted" : "completed";
            dialog.LogMessage($"Uninstall {status}.");
        });
        SyncModListView();
    }

    private void SyncModListView()
    {
        modList.Clear();
        foreach (var modState in modManager.FetchState().OrderBy(_ => _.PackageName))
        {
            modList.Add(new ModVM(modState, modManager));
        }
    }

    private void ModListView_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = !isDragging && e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ModListView_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!isDragging && e.DataTransfer.TryGetFiles() is { } files)
        {
            var paths = files.SelectNotNull(file => file.TryGetLocalPath()).ToArray();
            await AddNewModsAsync(paths);
        }
    }

    private void ModListView_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        dragPress = null;
        var source = e.Source as Visual;
        var item = source?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (item?.DataContext is not ModVM mod)
        {
            return;
        }

        var point = e.GetCurrentPoint(ModListView);
        if (point.Properties.IsRightButtonPressed)
        {
            // Preserve a multi-selection when opening its context menu.
            if (ModListView.SelectedItems?.Contains(mod) != true)
            {
                ModListView.SelectedItem = mod;
            }
        }
        else if (point.Properties.IsLeftButtonPressed &&
                 source?.FindAncestorOfType<ToggleButton>(includeSelf: true) is null)
        {
            dragPress = e;
            dragStart = point.Position;
        }
    }

    private async void ModListView_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (isDragging || dragPress is null || !e.GetCurrentPoint(ModListView).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var delta = e.GetPosition(ModListView) - dragStart;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
        {
            return;
        }

        var trigger = dragPress;
        dragPress = null;
        isDragging = true;
        try
        {
            var data = new DataTransfer();
            var paths = SelectedMods().SelectNotNull(mod => mod.PackagePath).ToArray();
            foreach (var path in paths)
            {
                // Keep directory mods in place, as in the original UI.
                if (File.Exists(path) && await StorageProvider.TryGetFileFromPathAsync(path) is { } file)
                {
                    data.Add(DataTransferItem.CreateFile(file));
                }
            }

            if (data.Items.Count > 0)
            {
                await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Copy | DragDropEffects.Move);
                SyncModListView();
            }
        }
        finally
        {
            isDragging = false;
        }
    }

    private IEnumerable<ModVM> SelectedMods() =>
        ModListView.SelectedItems?.OfType<ModVM>() ?? Enumerable.Empty<ModVM>();

    private void ModListMenuEnable_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var mod in SelectedMods())
        {
            mod.IsEnabled = true;
        }
    }

    private void ModListMenuDisable_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var mod in SelectedMods())
        {
            mod.IsEnabled = false;
        }
    }

    private async void ModListMenuAdd_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add mods",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.All]
        });
        await AddNewModsAsync(files.SelectNotNull(file => file.TryGetLocalPath()).ToArray());
    }

    private async void ModListMenuDelete_Click(object? sender, RoutedEventArgs e)
    {
        await DeleteSelectedModsAsync();
    }

    private async void ModListView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            await DeleteSelectedModsAsync();
        }
    }

    public async void SignalErrorAsync(Exception exception)
    {
        if (isShowingError)
        {
            return;
        }

        isShowingError = true;
        try
        {
            await new ErrorDialog(exception).ShowDialog(this);
        }
        finally
        {
            Close();
        }
    }

    private async Task AddNewModsAsync(IEnumerable<string> filePaths)
    {
        var paths = filePaths.ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        await SyncDialog.ShowAsync(this, paths, (dialog, path) =>
        {
            modManager.AddNewMod(path);
            dialog.LogMessage(Path.GetFileName(path));
        });
        SyncModListView();
    }

    private async Task DeleteSelectedModsAsync()
    {
        // Snapshot selection before the background operation starts.
        var paths = SelectedMods().SelectNotNull(mod => mod.PackagePath).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        await SyncDialog.ShowAsync(this, paths, (dialog, path) =>
        {
            modManager.DeleteMod(path);
            dialog.LogMessage(Path.GetFileName(path));
        });
        SyncModListView();
    }

    internal class SyncDialogEventLogger : BaseEventLogger
    {
        private readonly SyncDialog dialog;

        internal SyncDialogEventLogger(SyncDialog dialog)
        {
            this.dialog = dialog;
        }

        public override void ProgressUpdate(IPercent? value) => dialog.SetProgress(value?.Percent);
        protected override void LogMessage(string message) => dialog.LogMessage(message);
    }
}
