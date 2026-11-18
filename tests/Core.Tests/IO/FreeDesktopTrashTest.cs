using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Core.IO;
using FluentAssertions;

namespace Core.Tests.IO;

[SupportedOSPlatform("linux")]
public class FreeDesktopTrashTest
{
    #region Setup

    private const string RelativeFilePath = "-file.zip";
    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "file ; $(name).zip");
    private readonly Mock<ICommandRunner> runner = new(MockBehavior.Strict);
    private readonly FreeDesktopTrash trash;

    public FreeDesktopTrashTest()
    {
        trash = new FreeDesktopTrash(runner.Object);
    }

    #endregion

    [Fact]
    public void SafeDelete_PassesPathAsSeparateGioArgument()
    {
        runner.Setup(r => r.Run(It.Is<ProcessStartInfo>(info =>
                info.FileName == "gio" && !info.UseShellExecute && info.CreateNoWindow && info.RedirectStandardError &&
                info.ArgumentList.SequenceEqual(new[] { "trash", "--", FilePath }))))
            .Returns(new CommandResult(0, ""));

        trash.SafeDelete(FilePath);

        runner.VerifyAll();
    }

    [Fact]
    public void SafeDelete_ConvertsRelativePathToAbsolute()
    {
        var fullPath = Path.GetFullPath(RelativeFilePath);
        runner.Setup(r => r.Run(It.Is<ProcessStartInfo>(info => info.ArgumentList[2] == fullPath)))
            .Returns(new CommandResult(0, ""));

        trash.SafeDelete(RelativeFilePath);

        runner.VerifyAll();
    }

    [Fact]
    public void SafeDelete_CommandFailureReportsDiagnostic()
    {
        runner.Setup(r => r.Run(It.IsAny<ProcessStartInfo>())).Returns(new CommandResult(1, "Denied"));

        var delete = () => trash.SafeDelete(FilePath);

        delete.Should().Throw<IOException>().WithMessage($"*{FilePath}*code 1*Denied*");
    }

    [Fact]
    public void SafeDelete_MissingGioExplainsPrerequisite()
    {
        runner.Setup(r => r.Run(It.IsAny<ProcessStartInfo>())).Throws(new Win32Exception(2));

        var delete = () => trash.SafeDelete(FilePath);

        delete.Should().Throw<NotSupportedException>().WithMessage("*gio*Install*GIO*")
            .WithInnerException<Win32Exception>();
    }

    [Fact]
    public void SafeDelete_LaunchFailureReportsPath()
    {
        runner.Setup(r => r.Run(It.IsAny<ProcessStartInfo>())).Throws(new Win32Exception(13));

        var delete = () => trash.SafeDelete(FilePath);

        delete.Should().Throw<IOException>().WithMessage($"*{FilePath}*")
            .WithInnerException<Win32Exception>();
    }
}
