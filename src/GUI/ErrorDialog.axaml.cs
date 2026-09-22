using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace AMS2CM.GUI;

public sealed partial class ErrorDialog : Window
{
    private readonly string details;

    public ErrorDialog(Exception exception)
    {
        InitializeComponent();
        Message.Text = exception.Message;
        details = FormatDetails(exception);
    }

    private async void CopyDetails_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(details);
        }
    }

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    private static string FormatDetails(Exception exception) =>
        $@"**Version**: {GitVersionInformation.InformationalVersion}
**OS**: {Environment.OSVersion.VersionString}
```
{exception.Message}
{exception.StackTrace}
```";
}
