using System.IO.Abstractions.TestingHelpers;
using Core.Mods;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Tests.Packages.Installation.Installers;
using Core.Tests.Utils;
using Core.Utils;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Core.Tests.Mods.Installation.Installers;

[UnitTest]
public class BootfilesInstallerTest
{
    #region Setup

    private const string BootfilesPackageName = "notused";
    private static readonly string FileInBootfilesPackage = Path.Combine(GameDirAtRoot, "Something");

    private const string GameDirAtRoot = "DirAtRoot";
    private const string GameSupportedModDirectory = "ModDirectory";
    private const string BootfilesVehicleListDir = "BootfilesVehicleListDir";
    private const string VehicleListFileName = "VehicleList";
    private const string BootfilesTrackListDir = "BootfilesTrackListDir";
    private const string TrackListFileName = "TrackList";
    private const string BootfilesDrivelineDir = "BootfilesDrivelineDir";
    private const string DrivelineFileName = "Driveline";

    private readonly MockFileSystem fs = new();
    private readonly FakeTimeProvider fakeTimeProvider = new();
    private readonly Mock<BootfilesInstaller.IConfig> configMock = new();
    private readonly Mock<IBootfilesNaming> bootfilesNamingMock = new();
    private readonly Mock<BootfilesInstaller.IEventHandler> eventHandlerMock = new();
    private readonly Mock<IBackupStrategy> backupStrategyMock = new();
    private readonly Mock<Action<RootedPath>> callbackMock = new();

    private readonly string destinationPath;
    private readonly PathRoot destinationRoot;

    public BootfilesInstallerTest()
    {
        configMock.Setup(c => c.DirsAtRoot).Returns([GameDirAtRoot, GameSupportedModDirectory]);
        configMock.Setup(c => c.GameSupportedModDir).Returns(GameSupportedModDirectory);
        configMock.Setup(c => c.BootfilesVehicleListDir).Returns(BootfilesVehicleListDir);
        configMock.Setup(c => c.VehicleListFileName).Returns(VehicleListFileName);
        configMock.Setup(c => c.BootfilesTrackListDir).Returns(BootfilesTrackListDir);
        configMock.Setup(c => c.TrackListFileName).Returns(TrackListFileName);
        configMock.Setup(c => c.BootfilesDrivelineDir).Returns(BootfilesDrivelineDir);
        configMock.Setup(c => c.DrivelineFileName).Returns(DrivelineFileName);

        var destDir = fs.Directory.CreateDirectory("Dest");
        destinationPath = destDir.FullName;
        destinationRoot = new PathRoot(destDir);
    }

    #endregion

    [Fact]
    public void BootfilesAreNotInstalledWhenNoMods()
    {
        InstallBootfiles().InstalledFiles.Should().BeEmpty();

        eventHandlerMock.Verify(m => m.PostProcessingNotRequired(), Times.Once);

        callbackMock.VerifyNoOtherCalls();

        fs.AllFiles.Should().BeEmpty();
    }

    [Fact]
    public void BootfilesAreNotInstalledForModsWithManifests()
    {
        var contents = MultiLineContents();

        fs.AddEmptyFile(Path.Combine(destinationPath, GameSupportedModDirectory, "ModName", "ModName.xml"));
        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "ModName", VehicleListFileName), contents);
        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "ModName", TrackListFileName), contents);
        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "ModName", DrivelineFileName), contents);

        InstallBootfiles().InstalledFiles.Should().BeEmpty();

        eventHandlerMock.Verify(m => m.PostProcessingNotRequired(), Times.Once);

        callbackMock.VerifyNoOtherCalls();

        fs.File.Exists(Path.Combine(destinationPath, BootfilesVehicleListDir, VehicleListFileName)).Should().BeFalse();
        fs.File.Exists(Path.Combine(destinationPath, BootfilesTrackListDir, TrackListFileName)).Should().BeFalse();
        fs.File.Exists(Path.Combine(destinationPath, BootfilesDrivelineDir, DrivelineFileName)).Should().BeFalse();
    }

    [Fact]
    public void BootfilesAreInstalledForVehicleListWithoutModManifest()
    {
        var mod1Config = MultiLineContents();
        var mod2Config = MultiLineContents();

        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "Mod1", VehicleListFileName), mod1Config);
        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "Mod2", VehicleListFileName), mod2Config);

        var expected = DestRootedPaths(
            FileInBootfilesPackage,
            Path.Combine(BootfilesVehicleListDir, VehicleListFileName)
        );

        var bootfiles = InstallBootfiles();
        bootfiles.InstalledFiles.Should().BeEquivalentPathTo(expected);
        bootfiles.PackageVersionHash.Should().Be(42);

        eventHandlerMock.Verify(m => m.PostProcessingStart(), Times.Once);
        eventHandlerMock.Verify(m => m.ExtractingBootfiles(null), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingVehicles(), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingEnd(), Times.Once);
        eventHandlerMock.VerifyNoOtherCalls();

        VerifyCallbackCalledWith(expected);

        TrimConfig(fs.File.ReadAllText(Path.Combine(destinationPath, BootfilesVehicleListDir, VehicleListFileName)))
            .Should().Be(mod1Config + Environment.NewLine + mod2Config);
    }

    [Fact]
    public void BootfilesAreInstalledForTrackListWithoutModManifest()
    {
        var mod1Config = MultiLineContents();
        var mod2Config = MultiLineContents();

        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "Mod1", TrackListFileName), mod1Config);
        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "Mod2", TrackListFileName), mod2Config);

        var expected = DestRootedPaths(
            FileInBootfilesPackage,
            Path.Combine(BootfilesTrackListDir, TrackListFileName)
        );

        InstallBootfiles().InstalledFiles.Should().BeEquivalentPathTo(expected);

        eventHandlerMock.Verify(m => m.PostProcessingStart(), Times.Once);
        eventHandlerMock.Verify(m => m.ExtractingBootfiles(null), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingTracks(), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingEnd(), Times.Once);
        eventHandlerMock.VerifyNoOtherCalls();

        VerifyCallbackCalledWith(expected);

        TrimConfig(fs.File.ReadAllText(Path.Combine(destinationPath, BootfilesTrackListDir, TrackListFileName)))
            .Should().Be(mod1Config + Environment.NewLine + mod2Config);
    }

    [Fact]
    public void BootfilesAreInstalledForDrivelineWithoutModManifest()
    {
        var mod1Config = MultiLineContents();
        var mod2Config = MultiLineContents();

        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "Mod1", DrivelineFileName), mod1Config);
        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "Mod2", DrivelineFileName), mod2Config);

        var expected = DestRootedPaths(
            FileInBootfilesPackage,
            Path.Combine(BootfilesDrivelineDir, DrivelineFileName)
        );

        InstallBootfiles().InstalledFiles.Should().BeEquivalentPathTo(expected);

        eventHandlerMock.Verify(m => m.PostProcessingStart(), Times.Once);
        eventHandlerMock.Verify(m => m.ExtractingBootfiles(null), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingDrivelines(), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingEnd(), Times.Once);
        eventHandlerMock.VerifyNoOtherCalls();

        VerifyCallbackCalledWith(expected);

        TrimConfig(fs.File.ReadAllText(Path.Combine(destinationPath, BootfilesDrivelineDir, DrivelineFileName)))
            .Should().Be(mod1Config + Environment.NewLine + mod2Config);
    }


    [Fact]
    public void ConfigurationCanBeCalledWithoutInstallingBootfiles()
    {
        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "Mod", VehicleListFileName), "Vehicle.crd");
        var installer = CreateBootfiles();

        installer.Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        installer.Should().BeAssignableTo<IConfigurableInstaller>();
        TrimConfig(fs.File.ReadAllText(Path.Combine(destinationPath, BootfilesVehicleListDir, VehicleListFileName)))
            .Should().Be("Vehicle.crd");
        installer.InstalledFiles.Should().BeEquivalentPathTo(
            DestRootedPaths(Path.Combine(BootfilesVehicleListDir, VehicleListFileName)));
        eventHandlerMock.Verify(m => m.ExtractingBootfiles(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void RepeatedConfigurationDoesNotWriteUnchangedFiles()
    {
        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "Mod", VehicleListFileName), "Vehicle.crd");
        var installer = InstallBootfiles();
        backupStrategyMock.Invocations.Clear();

        installer.Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        backupStrategyMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void ChangedEntriesReplacePreviousBlocks()
    {
        var source = Path.Combine(destinationPath, GameSupportedModDirectory, "Mod", VehicleListFileName);
        fs.AddFile(source, "Old.crd");
        var installer = InstallBootfiles();
        fs.File.WriteAllText(source, "New.crd");

        installer.Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        TrimConfig(fs.File.ReadAllText(Path.Combine(destinationPath, BootfilesVehicleListDir, VehicleListFileName)))
            .Should().Be("New.crd");
    }

    [Fact]
    public void EmptyCategoryRemovesPreviousBlocks()
    {
        var source = Path.Combine(destinationPath, GameSupportedModDirectory, "Mod", VehicleListFileName);
        var target = Path.Combine(destinationPath, BootfilesVehicleListDir, VehicleListFileName);
        const string original = "Original\r\n";
        fs.AddFile(target, original);
        fs.AddFile(source, "Vehicle.crd");
        var installer = InstallBootfiles();
        fs.File.Delete(source);

        installer.Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        fs.File.ReadAllText(target).Should().Be(original);
    }

    [Fact]
    public void MultipleBlocksAreRemovedWithoutChangingSurroundingText()
    {
        const string block = "\r\n### BEGIN AMS2CM\r\nVehicle.crd\r\n### END AMS2CM\r\n";

        BootfilesInstaller.RemoveConfigBlocks("Before\n" + block + "Between\r\n" + block + "After")
            .Should().Be("Before\nBetween\r\nAfter");
    }

    [Theory]
    [InlineData("### BEGIN AMS2CM")]
    [InlineData("### END AMS2CM")]
    [InlineData("### END AMS2CM\n### BEGIN AMS2CM")]
    [InlineData("### BEGIN AMS2CM\n### BEGIN AMS2CM\n### END AMS2CM")]
    public void MalformedBlocksLeaveFileUnchanged(string original)
    {
        var target = Path.Combine(destinationPath, BootfilesVehicleListDir, VehicleListFileName);
        fs.AddFile(target, original);
        var installer = CreateBootfiles();

        var configure = () => installer.Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        configure.Should().Throw<InvalidDataException>();
        fs.File.ReadAllText(target).Should().Be(original);
        backupStrategyMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void BackupFailureLeavesConfigurationUnchanged()
    {
        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "Mod", VehicleListFileName), "Vehicle.crd");
        var target = destinationRoot.Combine(BootfilesVehicleListDir, VehicleListFileName);
        fs.AddFile(target.Full, "Original");
        backupStrategyMock.Setup(b => b.PerformBackup(target)).Throws(new IOException("Backup failed"));
        var installer = CreateBootfiles();

        var configure = () => installer.Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        configure.Should().Throw<IOException>();
        fs.File.ReadAllText(target.Full).Should().Be("Original");
        installer.InstalledFiles.Should().BeEmpty();
    }

    [Fact]
    public void WriteFailureTracksAttemptedConfigurationFile()
    {
        fs.AddFile(Path.Combine(destinationPath, GameSupportedModDirectory, "Mod", VehicleListFileName), "Vehicle.crd");
        var target = destinationRoot.Combine(BootfilesVehicleListDir, VehicleListFileName);
        fs.Directory.CreateDirectory(target.Full);
        var installer = CreateBootfiles();

        var configure = () => installer.Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        configure.Should().Throw<UnauthorizedAccessException>();
        installer.InstalledFiles.Should().Contain(target);
        backupStrategyMock.Verify(b => b.AfterInstall(target), Times.Once);
    }

    #region Utility Methods


    private BootfilesInstaller InstallBootfiles()
    {
        var bootfilesInstaller = CreateBootfiles();
        bootfilesInstaller.Install(destinationRoot.Combine,
            backupStrategyMock.Object, new ProcessingCallbacks<RootedPath> { Before = callbackMock.Object });
        bootfilesInstaller.Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath> { Before = callbackMock.Object });
        fs.Directory.Delete("Temp", recursive: true);
        return bootfilesInstaller;
    }

    private BootfilesInstaller CreateBootfiles()
    {
        var tempDir = fs.Directory.CreateDirectory("Temp");
        var emptyPackage = new StaticFilesInstaller(fs, fakeTimeProvider, BootfilesPackageName, 42,
            new Dictionary<string, string> { [FileInBootfilesPackage] = "" }, Array.Empty<string>());
        return new BootfilesInstaller(emptyPackage, tempDir, configMock.Object,
            fs.DirectoryInfo.New(destinationPath), bootfilesNamingMock.Object, eventHandlerMock.Object);
    }

    private static string MultiLineContents()
    {
        var rnd = new Random();
        var options = Enumerable.Range(0, 128).Select(i => (char)i)
            .Where(char.IsAsciiLetterOrDigit).Select(c => c.ToString())
            .Append(Environment.NewLine).ToArray();
        var length = rnd.Next(10, 100);
        var randomConfig = string.Concat(Enumerable.Range(0, length).Select(_ => options[rnd.Next(options.Length)]));
        return TrimConfig(randomConfig);
    }

    private static string TrimConfig(string config) =>
        string.Join(Environment.NewLine,
            config.Split(Environment.NewLine)
                .Select(line => line.Split('#')[0].Trim())
                .Where(line => !string.IsNullOrEmpty(line)));

    private HashSet<RootedPath> DestRootedPaths(params string[] relativePaths) =>
        [.. relativePaths.Select(destinationRoot.Combine)];

    private void VerifyCallbackCalledWith(IReadOnlySet<RootedPath> relativePaths)
    {
        foreach (var rp in relativePaths)
        {
            callbackMock.Verify(a => a(rp), Times.Once);
        }

        callbackMock.VerifyNoOtherCalls();
    }

    #endregion
}
