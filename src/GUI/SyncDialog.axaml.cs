using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Core.Utils;

namespace AMS2CM.GUI;

public sealed partial class SyncDialog : Window
{
    private readonly CancellationTokenSource cancellationTokenSource;
    private bool isRunning = true;

    private SyncDialog(CancellationTokenSource cancellationTokenSource)
    {
        InitializeComponent();
        this.cancellationTokenSource = cancellationTokenSource;
    }

    private void AbortButton_Click(object? sender, RoutedEventArgs e) => RequestCancellation();

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (isRunning)
        {
            e.Cancel = true;
            RequestCancellation();
        }
    }

    private void RequestCancellation()
    {
        if (cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        AbortButton.IsEnabled = false;
        Logs.Text += $"Aborting...{Environment.NewLine}";
        cancellationTokenSource.Cancel();
    }

    public void SetProgress(double? progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Progress.IsIndeterminate = !progress.HasValue;
            Progress.Value = progress.GetValueOrDefault() * 100;
        });
    }

    public void LogMessage(string message)
    {
        Dispatcher.UIThread.Post(() => Logs.Text += $"{message}{Environment.NewLine}");
    }

    public void LogError(Exception ex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Progress.IsIndeterminate = false;
            Progress.Foreground = Brushes.IndianRed;
            LogExpander.IsExpanded = true;
            Logs.Text += $"Error: {ex.Message}{Environment.NewLine}";
        });
    }

    /// <summary>Execute an action for each item and close automatically on success.</summary>
    public static Task ShowAsync<T>(Window owner, IEnumerable<T> enumerable, Action<SyncDialog, T> action)
    {
        var items = enumerable.ToList();
        return ShowAsync(owner, (dialog, token) =>
        {
            var progress = new PercentOfTotal(items.Count);
            foreach (var item in items)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                action(dialog, item);
                dialog.SetProgress(progress.IncrementDone().Percent);
            }
            return true;
        });
    }

    /// <summary>Execute an action and leave the log open until the user closes it.</summary>
    public static Task ShowAsync(Window owner, Action<SyncDialog, CancellationToken> action) =>
        ShowAsync(owner, (dialog, token) =>
        {
            action(dialog, token);
            return false;
        });

    /// <summary>Execute work off the UI thread; failures always leave the log open.</summary>
    public static async Task ShowAsync(Window owner, Func<SyncDialog, CancellationToken, bool> func)
    {
        using var cancellation = new CancellationTokenSource();
        var dialog = new SyncDialog(cancellation);
        var closed = dialog.ShowDialog(owner);

        var shouldClose = await Task.Run(() =>
        {
            try
            {
                return func(dialog, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                dialog.LogMessage("Operation aborted.");
                return false;
            }
            catch (Exception ex)
            {
                dialog.LogError(ex);
                return false;
            }
        });

        // Drain queued progress and log updates before enabling Close or closing automatically.
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            dialog.isRunning = false;
            dialog.AbortButton.IsEnabled = false;
            dialog.CloseButton.IsEnabled = true;
            dialog.Progress.IsIndeterminate = false;

            if (shouldClose)
            {
                dialog.Close();
            }
        }, DispatcherPriority.Background);

        await closed;
    }
}
