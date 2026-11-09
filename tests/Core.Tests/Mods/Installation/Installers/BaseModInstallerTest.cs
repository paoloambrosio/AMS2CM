using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Core.API;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;
using FluentAssertions;

namespace Core.Tests.Mods.Installation.Installers;

[UnitTest]
public class BaseModInstallerTest
{
    #region Setup

    private readonly MockFileSystem fs = new();
    private readonly Mock<IPackageInstaller> inner = new(MockBehavior.Strict);
    private readonly Mock<Action> configure = new(MockBehavior.Strict);
    private readonly IBackupStrategy backupStrategy = Mock.Of<IBackupStrategy>();

    public BaseModInstallerTest()
    {
        inner.SetupGet(i => i.PackageName).Returns("Package");
    }

    #endregion

    [Fact]
    public void InstallConfiguresAfterExtraction()
    {
        var sequence = new MockSequence();
        inner.InSequence(sequence).Setup(i => i.Install(It.IsAny<IInstaller.Destination>(), backupStrategy,
            It.IsAny<ProcessingCallbacks<RootedPath>>()));
        configure.InSequence(sequence).Setup(c => c());
        var installer = new TestInstaller(inner.Object, fs.Directory.CreateDirectory("Temp"), configure.Object);

        installer.Install(new PathRoot(fs.Directory.CreateDirectory("Destination")).Combine,
            backupStrategy, new ProcessingCallbacks<RootedPath>());

        inner.Verify(i => i.Install(It.IsAny<IInstaller.Destination>(), backupStrategy,
            It.IsAny<ProcessingCallbacks<RootedPath>>()), Times.Once);
        configure.Verify(c => c(), Times.Once);
    }

    [Fact]
    public void InstallSkipsConfigurationWhenExtractionFails()
    {
        inner.Setup(i => i.Install(It.IsAny<IInstaller.Destination>(), backupStrategy,
            It.IsAny<ProcessingCallbacks<RootedPath>>())).Throws(new IOException("Extraction failed"));
        var installer = new TestInstaller(inner.Object, fs.Directory.CreateDirectory("Temp"), configure.Object);

        var install = () => installer.Install(new PathRoot(fs.Directory.CreateDirectory("Destination")).Combine,
            backupStrategy, new ProcessingCallbacks<RootedPath>());

        install.Should().Throw<IOException>().WithMessage("Extraction failed");
        configure.VerifyNoOtherCalls();
    }

    [Fact]
    public void DedupeRecordBlocks_ConsidersOnlyFirstLine()
    {
        BaseModInstaller.DedupeRecordBlocks([
            @"RECORDGROUP foo
                first",
            @"RECORDGROUP foo
                last"
        ]).Should().BeEquivalentTo([
            @"RECORDGROUP foo
                last"
        ]);
    }

    [Fact]
    public void DedupeRecordBlocks_IgnoresRedundantWhitespaces()
    {
        BaseModInstaller.DedupeRecordBlocks([
            "  RECORD foo\vbar ",
            "RECORD foo\tbar"
        ]).Should().BeEquivalentTo([
            "RECORD foo\tbar"
        ]);
    }

    [Fact]
    public void DedupeRecordBlocks_WorksForEmptyLines()
    {
        BaseModInstaller.DedupeRecordBlocks([
            "",
            "",
        ]).Should().BeEquivalentTo([
            ""
        ]);
    }

    [Fact]
    public void DedupeRecordBlocks_AssumesCommentsAlreadyRemoved()
    {
        BaseModInstaller.DedupeRecordBlocks([
            @"RECORD foo bar # first",
            @"RECORD foo bar # last"
        ]).Should().BeEquivalentTo([
            @"RECORD foo bar # first",
            @"RECORD foo bar # last"
        ]);
    }
    #region Utility Methods

    private sealed class TestInstaller(IPackageInstaller inner, IDirectoryInfo tempDir, Action configure)
        : BaseModInstaller(inner, tempDir, new ModInstallConfig())
    {
        public override IReadOnlySet<string> PackageDependencies => new HashSet<string>();
        protected override RootedPath VehicleListDir => StagingRoot.Combine("Vehicles");
        protected override RootedPath TrackListDir => StagingRoot.Combine("Tracks");
        protected override RootedPath DrivelineDir => StagingRoot.Combine("Drivelines");

        protected override void Configure(IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks) =>
            configure();

        protected override void Reconfigure(IBackupStrategy backupStrategy,
            ProcessingCallbacks<RootedPath> callbacks) =>
            configure();
    }

    #endregion
}
