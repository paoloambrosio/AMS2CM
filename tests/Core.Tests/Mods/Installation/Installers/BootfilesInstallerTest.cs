using System.IO.Abstractions.TestingHelpers;
using Core.Mods;
using Core.Mods.Installation.Installers;
using Core.Mods.Installation;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Tests.Packages.Installation.Installers;
using Core.Utils;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Core.Tests.Mods.Installation.Installers;

[UnitTest]
public class BootfilesInstallerTest
{
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

    #region Setup

    private readonly MockFileSystem fs = new();
    private readonly FakeTimeProvider fakeTimeProvider = new();
    private readonly Mock<BootfilesInstaller.IConfig> configMock = new();
    private readonly Mock<IBootfilesNaming> bootfilesNamingMock = new();
    private readonly Mock<BootfilesInstaller.IEventHandler> eventHandlerMock = new();
    private readonly Mock<IBackupStrategy> backupStrategyMock = new();
    private readonly Mock<Action<RootedPath>> callbackMock = new();

    private readonly string destDir;
    private readonly string tempDir;

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

        destDir = fs.Directory.CreateDirectory("Dest").FullName;
        tempDir = fs.Directory.CreateDirectory("Temp").FullName;
    }

    #endregion

    [Fact]
    public void ConfigurationDoesNotCreateFilesWhenNoMods()
    {
        NewBootfilesInstaller().Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        eventHandlerMock.Verify(m => m.PostProcessingStart(), Times.Once);

        callbackMock.VerifyNoOtherCalls();

        fs.AllFiles.Should().BeEmpty();
    }

    [Fact]
    public void ConfigurationIgnoresModsWithManifests()
    {
        var contents = MultiLineContents();

        fs.AddEmptyFile(Path.Combine(destDir, GameSupportedModDirectory, "ModName", "ModName.xml"));
        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "ModName", VehicleListFileName), contents);
        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "ModName", TrackListFileName), contents);
        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "ModName", DrivelineFileName), contents);

        NewBootfilesInstaller().Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        eventHandlerMock.Verify(m => m.PostProcessingStart(), Times.Once);

        callbackMock.VerifyNoOtherCalls();

        fs.File.Exists(Path.Combine(destDir, BootfilesVehicleListDir, VehicleListFileName)).Should().BeFalse();
        fs.File.Exists(Path.Combine(destDir, BootfilesTrackListDir, TrackListFileName)).Should().BeFalse();
        fs.File.Exists(Path.Combine(destDir, BootfilesDrivelineDir, DrivelineFileName)).Should().BeFalse();
    }

    [Fact]
    public void BootfilesAreInstalledForVehicleListWithoutModManifest()
    {
        var mod1Config = MultiLineContents();
        var mod2Config = MultiLineContents();

        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "Mod1", VehicleListFileName), mod1Config);
        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "Mod2", VehicleListFileName), mod2Config);

        var expected = DestRootedPaths(
            FileInBootfilesPackage,
            Path.Combine(BootfilesVehicleListDir, VehicleListFileName)
        );

        var bootfiles = InstallBootfiles();
        bootfiles.InstalledFiles.Should().BeEquivalentTo(DestRootedPaths(FileInBootfilesPackage));
        bootfiles.PackageVersionHash.Should().Be(42);

        eventHandlerMock.Verify(m => m.PostProcessingStart(), Times.Once);
        eventHandlerMock.Verify(m => m.ExtractingBootfiles(BootfilesPackageName), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingVehicles(), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingEnd(), Times.Once);
        eventHandlerMock.VerifyNoOtherCalls();

        VerifyCallbackCalledWith(expected);

        TrimConfig(fs.File.ReadAllText(Path.Combine(destDir, BootfilesVehicleListDir, VehicleListFileName)))
            .Should().Be(mod1Config + Environment.NewLine + mod2Config);
    }

    [Fact]
    public void BootfilesAreInstalledForTrackListWithoutModManifest()
    {
        var mod1Config = MultiLineContents();
        var mod2Config = MultiLineContents();

        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "Mod1", TrackListFileName), mod1Config);
        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "Mod2", TrackListFileName), mod2Config);

        var expected = DestRootedPaths(
            FileInBootfilesPackage,
            Path.Combine(BootfilesTrackListDir, TrackListFileName)
        );

        InstallBootfiles().InstalledFiles.Should().BeEquivalentTo(DestRootedPaths(FileInBootfilesPackage));

        eventHandlerMock.Verify(m => m.PostProcessingStart(), Times.Once);
        eventHandlerMock.Verify(m => m.ExtractingBootfiles(BootfilesPackageName), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingTracks(), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingEnd(), Times.Once);
        eventHandlerMock.VerifyNoOtherCalls();

        VerifyCallbackCalledWith(expected);

        TrimConfig(fs.File.ReadAllText(Path.Combine(destDir, BootfilesTrackListDir, TrackListFileName)))
            .Should().Be(mod1Config + Environment.NewLine + mod2Config);
    }

    [Fact]
    public void BootfilesAreInstalledForDrivelineWithoutModManifest()
    {
        var mod1Config = MultiLineContents();
        var mod2Config = MultiLineContents();

        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "Mod1", DrivelineFileName), mod1Config);
        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "Mod2", DrivelineFileName), mod2Config);

        var expected = DestRootedPaths(
            FileInBootfilesPackage,
            Path.Combine(BootfilesDrivelineDir, DrivelineFileName)
        );

        InstallBootfiles().InstalledFiles.Should().BeEquivalentTo(DestRootedPaths(FileInBootfilesPackage));

        eventHandlerMock.Verify(m => m.PostProcessingStart(), Times.Once);
        eventHandlerMock.Verify(m => m.ExtractingBootfiles(BootfilesPackageName), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingDrivelines(), Times.Once);
        eventHandlerMock.Verify(m => m.PostProcessingEnd(), Times.Once);
        eventHandlerMock.VerifyNoOtherCalls();

        VerifyCallbackCalledWith(expected);

        TrimConfig(fs.File.ReadAllText(Path.Combine(destDir, BootfilesDrivelineDir, DrivelineFileName)))
            .Should().Be(mod1Config + Environment.NewLine + mod2Config);
    }


    [Fact]
    public void ConfigurationIsRepeatableAndRemovesObsoleteCategories()
    {
        var vehicleSource = Path.Combine(destDir, GameSupportedModDirectory, "Mod", VehicleListFileName);
        var trackSource = Path.Combine(destDir, GameSupportedModDirectory, "Mod", TrackListFileName);
        var drivelineSource = Path.Combine(destDir, GameSupportedModDirectory, "Mod", DrivelineFileName);
        var vehicle = Path.Combine(destDir, BootfilesVehicleListDir, VehicleListFileName);
        var track = Path.Combine(destDir, BootfilesTrackListDir, TrackListFileName);
        var driveline = Path.Combine(destDir, BootfilesDrivelineDir, DrivelineFileName);
        fs.AddFile(vehicleSource, "Vehicle1");
        fs.AddFile(trackSource, "Track1");
        fs.AddFile(drivelineSource, "RECORD Test");
        fs.AddFile(vehicle, "Original vehicle\r\n");
        fs.AddFile(track, "Original track\r\n");
        fs.AddFile(driveline, "Original driveline\r\nEND");
        var installer = NewBootfilesInstaller();
        var callbacks = new ProcessingCallbacks<RootedPath>();

        installer.Configure(backupStrategyMock.Object, callbacks);
        var first = fs.AllFiles.ToDictionary(p => p, p => fs.File.ReadAllText(p));
        installer.Configure(backupStrategyMock.Object, callbacks);
        fs.AllFiles.ToDictionary(p => p, p => fs.File.ReadAllText(p)).Should().BeEquivalentTo(first);

        fs.File.WriteAllText(vehicleSource, "Vehicle2");
        fs.File.Delete(trackSource);
        fs.File.Delete(drivelineSource);
        installer.Configure(backupStrategyMock.Object, callbacks);

        fs.File.ReadAllText(vehicle).Should().StartWith("Original vehicle\r\n").And.Contain("Vehicle2").And.NotContain("Vehicle1");
        fs.File.ReadAllText(track).Should().Be("Original track\r\n");
        fs.File.ReadAllText(driveline).Should().Be("Original driveline\r\nEND");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ConfigurationReplacesMultipleBlocksAndPreservesOriginalNewlines(string newline)
    {
        var original = $"Original{newline}Second line{newline}";
        var oldBlock = $"{newline}### BEGIN AMS2CM{newline}Old{newline}### END AMS2CM{newline}";

        var updated = BootfilesInstaller.ConfigureListContents(original + oldBlock + oldBlock, "New");

        updated.Should().StartWith(original).And.Contain("New").And.NotContain("Old");
        BootfilesInstaller.RemoveConfigBlocks(updated).Should().Be(original);
        BootfilesInstaller.ConfigureListContents(updated, "New").Should().Be(updated);
        BootfilesInstaller.ConfigureListContents(updated, "").Should().Be(original);
    }

    [Fact]
    public void ConfigurationRejectsMissingDrivelineInsertionPointWithoutChangingFile()
    {
        var target = Path.Combine(destDir, BootfilesDrivelineDir, DrivelineFileName);
        fs.AddFile(target, "Original without terminator");
        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "Mod", DrivelineFileName), "RECORD Test");
        var configure = () => NewBootfilesInstaller().Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        configure.Should().Throw<InvalidDataException>().WithMessage($"*{target}");
        fs.File.ReadAllText(target).Should().Be("Original without terminator");
        backupStrategyMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("### BEGIN AMS2CM\nUnclosed")]
    [InlineData("### END AMS2CM")]
    [InlineData("### BEGIN AMS2CM\n### BEGIN AMS2CM\n### END AMS2CM")]
    public void ConfigurationRejectsMalformedBlocksWithoutChangingFile(string contents)
    {
        var target = Path.Combine(destDir, BootfilesVehicleListDir, VehicleListFileName);
        fs.AddFile(target, contents);
        var configure = () => NewBootfilesInstaller().Configure(backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        configure.Should().Throw<InvalidDataException>();
        fs.File.ReadAllText(target).Should().Be(contents);
        backupStrategyMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void ConfigurationDoesNotTouchHigherPriorityFiles()
    {
        var target = Path.Combine(destDir, BootfilesVehicleListDir, VehicleListFileName);
        fs.AddFile(target, "### BEGIN AMS2CM malformed but owned by another mod");
        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "Mod", VehicleListFileName), "Vehicle");

        NewBootfilesInstaller().Configure(backupStrategyMock.Object,
            new ProcessingCallbacks<RootedPath> { Accept = _ => false });

        fs.File.ReadAllText(target).Should().Be("### BEGIN AMS2CM malformed but owned by another mod");
        backupStrategyMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void ConfigurationActionPreservesBackupsTimeAndOwnershipThroughUninstall()
    {
        var target = Path.Combine(destDir, BootfilesVehicleListDir, VehicleListFileName);
        fs.AddFile(target, "Original");
        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "Mod", VehicleListFileName), "Vehicle");
        fs.AddFile(Path.Combine(destDir, FileInBootfilesPackage), "Bootfiles");
        var originalTime = DateTimeOffset.UtcNow.AddDays(-1);
        var state = new PackageInstallationState(originalTime, 42, false, [], [FileInBootfilesPackage], []);
        var backups = new MoveFileBackupStrategy(fs, new BackupNaming(), null);
        var action = new BootfilesConfigurationAction(NewBootfilesInstaller(), destDir, () => state, _ => backups);
        action.Execute(new ProcessingCallbacks<RootedPath>());

        action.InstallTime.Should().Be(originalTime);
        action.PackageVersionHash.Should().Be(42);
        action.InstalledFiles.Should().Contain(Path.Combine(BootfilesVehicleListDir, VehicleListFileName));
        fs.File.ReadAllText(target + ".orig").Should().Be("Original");
        state = state with { Files = action.InstalledFiles };
        // A kept bootfiles package has already claimed its files in reconciliation.
        var repeated = new BootfilesConfigurationAction(NewBootfilesInstaller(), destDir, () => state, _ => backups);
        repeated.Execute(new ProcessingCallbacks<RootedPath> { Accept = _ => false });
        repeated.InstallState.Should().Be(IInstallation.State.Installed);
        fs.File.ReadAllText(target + ".orig").Should().Be("Original");

        foreach (var file in repeated.InstalledFiles) backups.RestoreBackup(new RootedPath(destDir, file));
        fs.File.ReadAllText(target).Should().Be("Original");
        fs.File.Exists(target + ".orig").Should().BeFalse();
        fs.File.Exists(Path.Combine(destDir, FileInBootfilesPackage)).Should().BeFalse();
    }

    [Fact]
    public void ConfigurationFailureTracksAttemptedNewFiles()
    {
        var relative = Path.Combine(BootfilesVehicleListDir, VehicleListFileName);
        fs.AddFile(Path.Combine(destDir, GameSupportedModDirectory, "Mod", VehicleListFileName), "Vehicle");
        var state = new PackageInstallationState(DateTimeOffset.UtcNow, 42, false, [], [FileInBootfilesPackage], []);
        backupStrategyMock.Setup(b => b.PerformBackup(It.IsAny<RootedPath>())).Throws<IOException>();
        var action = new BootfilesConfigurationAction(NewBootfilesInstaller(), destDir, () => state, _ => backupStrategyMock.Object);

        action.Invoking(a => a.Execute(new ProcessingCallbacks<RootedPath>())).Should().Throw<IOException>();

        action.InstallState.Should().Be(IInstallation.State.PartiallyInstalled);
        action.InstalledFiles.Should().BeEquivalentTo([FileInBootfilesPackage, relative]);
    }

    private class BackupNaming : MoveFileBackupStrategy.IBackupFileNaming
    {
        public string ToBackup(string path) => path + ".orig";
        public bool IsBackup(string path) => path.EndsWith(".orig");
    }

    #region Utility Methods

    private BootfilesInstaller InstallBootfiles()
    {
        var bootfilesInstaller = NewBootfilesInstaller();
        var callbacks = new ProcessingCallbacks<RootedPath> { Before = callbackMock.Object };
        bootfilesInstaller.Install(packagePath => new RootedPath(destDir, packagePath), backupStrategyMock.Object, callbacks);
        bootfilesInstaller.Configure(backupStrategyMock.Object, callbacks);
        fs.Directory.Delete(tempDir, recursive: true);
        return bootfilesInstaller;
    }

    private BootfilesInstaller NewBootfilesInstaller()
    {
        var emptyPackage = new StaticFilesInstaller(fs, fakeTimeProvider, BootfilesPackageName, 42,
            new Dictionary<string, string>
            {
                [FileInBootfilesPackage] = ""
            }, Array.Empty<string>());
        return new BootfilesInstaller(fs, emptyPackage, tempDir, configMock.Object,
            destDir, bootfilesNamingMock.Object, eventHandlerMock.Object);
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

    private IReadOnlySet<RootedPath> DestRootedPaths(params string[] relativePaths) =>
        relativePaths.Select(f => new RootedPath(destDir, f)).ToHashSet();

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
