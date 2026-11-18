using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Core.IO;

/// <summary>
/// Implements <see cref="ISafeFileDelete"/> through GIO on freedesktop.org systems.
/// </summary>
/// <seealso href="https://specifications.freedesktop.org/trash-spec/trashspec-latest.html">FreeDesktop Trash Specification</seealso>
[SupportedOSPlatform("linux")]
public class FreeDesktopTrash : ISafeFileDelete
{
    private readonly ICommandRunner commandRunner;

    public FreeDesktopTrash() : this(new ProcessCommandRunner())
    {
    }

    internal FreeDesktopTrash(ICommandRunner commandRunner)
    {
        this.commandRunner = commandRunner;
    }

    public void SafeDelete(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var result = MoveToTrash(fullPath);
        if (result.ExitCode != 0)
        {
            throw new IOException(
                $"Cannot move '{fullPath}' to the trash. gio exited with code {result.ExitCode}: {result.StandardError.Trim()}");
        }
    }

    private CommandResult MoveToTrash(string fullPath)
    {
        var startInfo = new ProcessStartInfo("gio")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("trash");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(fullPath);
        try
        {
            return commandRunner.Run(startInfo);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 2)
        {
            throw new NotSupportedException("The gio command is unavailable. Install the GIO command-line tools to use the trash.", exception);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            throw new IOException($"Cannot move '{fullPath}' to the trash: {exception.Message}", exception);
        }
    }
}
