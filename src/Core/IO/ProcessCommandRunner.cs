using System.Diagnostics;

namespace Core.IO;

internal sealed class ProcessCommandRunner : ICommandRunner
{
    public CommandResult Run(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo) ?? throw new IOException("The command did not start.");
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new CommandResult(process.ExitCode, standardError);
    }
}
