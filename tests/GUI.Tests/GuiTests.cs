using AMS2CM.GUI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Core.API;
using Core.SoftwareUpdates;
using Moq;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(GUI.Tests.TestAppBuilder))]

namespace GUI.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public class GuiTests
{
    [AvaloniaFact]
    public void MainWindowLoadsSortedModsAndBindsEnableCheckbox()
    {
        var manager = new Mock<IModManager>();
        manager.Setup(m => m.FetchState()).Returns([
            new ModState("Zulu.zip", "Zulu.zip", false, false, false),
            new ModState("Alpha.zip", "Alpha.zip", null, false, true)
        ]);
        manager.Setup(m => m.EnableMod("Alpha.zip")).Returns("Alpha.zip");
        var updates = new Mock<IUpdateChecker>();
        updates.Setup(u => u.CheckUpdateAvailable()).ReturnsAsync(true);
        var window = new MainWindow(manager.Object, updates.Object);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = window.FindControl<ListBox>("ModListView")!;
            var first = list.Items[0]!;
            Assert.Equal("Alpha", first.GetType().GetProperty("DisplayName")!.GetValue(first));
            Assert.True(window.FindControl<StackPanel>("NewVersionBlock")!.IsVisible);
            var checkboxes = list.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.True(checkboxes.Length >= 2);
            Assert.Null(checkboxes[0].IsChecked);
            checkboxes[1].IsChecked = true;
            manager.Verify(m => m.EnableMod("Alpha.zip"), Times.Once);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SuccessfulBatchClosesAndProcessesEveryItem()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var processed = new List<int>();
            await SyncDialog.ShowAsync(owner, new[] { 1, 2, 3 }, (_, item) => processed.Add(item));
            Assert.Equal(new[] { 1, 2, 3 }, processed);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task FailureKeepsDialogOpenAndShowsError()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var completion = SyncDialog.ShowAsync(owner,
                (Func<SyncDialog, CancellationToken, bool>)((_, _) => throw new InvalidOperationException("Test failure")));
            var dialog = Assert.Single(owner.OwnedWindows.OfType<SyncDialog>());
            await WaitUntil(() => dialog.FindControl<Button>("CloseButton")!.IsEnabled);
            Assert.False(completion.IsCompleted);
            Assert.Contains("Test failure", dialog.FindControl<SelectableTextBlock>("Logs")!.Text);
            Assert.True(dialog.FindControl<Expander>("LogExpander")!.IsExpanded);
            dialog.Close();
            await completion;
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task ClosingDuringWorkCancelsAndWaitsForWorker()
    {
        var owner = new Window();
        owner.Show();
        using var finish = new ManualResetEventSlim();
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var completion = SyncDialog.ShowAsync(owner, (_, token) =>
            {
                if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Cancellation was not requested.");
                cancellationObserved.SetResult();
                if (!finish.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Worker was not released.");
            });
            var dialog = Assert.Single(owner.OwnedWindows.OfType<SyncDialog>());
            dialog.Close();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(dialog.IsVisible);
            Assert.False(completion.IsCompleted);
            Assert.False(dialog.FindControl<Button>("AbortButton")!.IsEnabled);
            finish.Set();
            await WaitUntil(() => dialog.FindControl<Button>("CloseButton")!.IsEnabled);
            dialog.Close();
            await completion;
        }
        finally
        {
            finish.Set();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void ErrorDialogLoadsExceptionMessage()
    {
        var dialog = new ErrorDialog(new InvalidOperationException("Test error"));
        try
        {
            dialog.Show();
            Assert.Equal("Test error", dialog.FindControl<SelectableTextBlock>("Message")!.Text);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
