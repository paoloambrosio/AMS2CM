using System.Diagnostics;

namespace Core.IO;

internal interface ICommandRunner
{
    CommandResult Run(ProcessStartInfo startInfo);
}

internal sealed record CommandResult(int ExitCode, string StandardError);
