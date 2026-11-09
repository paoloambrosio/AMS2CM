using System.IO.Abstractions.TestingHelpers;
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
public class ModInstallerTest
{
    #region Setup

    private const string GameSupportedModDirectory = "ModDirectory";
    private const string GameDirAtRoot = "DirAtRoot";
    private const string BootfilesPackageName = "BootFilesPackage";
    private const string VehicleListFile = "vehiclelist.lst";
    private const string TrackListFile = "tracklist.lst";
    private const string DrivelineFile = "driveline.rg";

    private static readonly Dictionary<char, char> InvertedCasing =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
            .Zip("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz")
            .ToDictionary(pair => pair.First, pair => pair.Second);

    private readonly MockFileSystem fs = new();
    private readonly FakeTimeProvider fakeTimeProvider = new();
    private readonly Mock<ModInstaller.IConfig> configMock = new();
    private readonly Mock<IBackupStrategy> backupStrategyMock = new();
    private readonly Mock<Action<RootedPath>> callbackMock = new();

    private readonly string destinationPath;
    private readonly PathRoot destinationRoot;

    public ModInstallerTest()
    {
        var gsmd = InvertCasing(GameSupportedModDirectory);
        configMock.Setup(c => c.DirsAtRoot).Returns([InvertCasing(GameDirAtRoot), gsmd]);
        configMock.Setup(c => c.GameSupportedModDir).Returns(gsmd);
        configMock.Setup(c => c.GenerateModDetails).Returns(true);
        configMock.Setup(c => c.VehicleListFileName).Returns(VehicleListFile);
        configMock.Setup(c => c.TrackListFileName).Returns(TrackListFile);
        configMock.Setup(c => c.DrivelineFileName).Returns(DrivelineFile);
        backupStrategyMock.Setup(b => b.RestoreBackup(It.IsAny<RootedPath>()))
            .Callback<RootedPath>(path => fs.File.Delete(path.Full));

        var destDir = fs.Directory.CreateDirectory("Dest");
        destinationPath = destDir.FullName;
        destinationRoot = new PathRoot(destDir);
    }

    #endregion

    [Fact]
    public void AutomaticModConfigurationSkippedWhenNothingToConfigure()
    {
        string[] packageFiles =
        [
            Path.Combine(GameDirAtRoot, "NotRequiringConfiguration"),
        ];

        var modInstaller = InstallWithModInstaller(InstallerOf("A", null, packageFiles));

        modInstaller.InstalledFiles.Should().BeEquivalentPathTo(ToDestRootedPath(packageFiles));
        modInstaller.PackageDependencies.Should().BeEmpty();

        VerifyCallbackCalledWith(packageFiles);

        fs.AllFiles.Should().BeEquivalentPathTo(ToDestPath(packageFiles));
    }

    [Fact]
    public void AutomaticModConfigurationSkippedWhenConfiguredByPackage()
    {
        string[] packageFiles =
        [
            Path.Combine(GameDirAtRoot, "File.crd"),
            // This disables configuration
            Path.Combine(GameSupportedModDirectory, "Anything")
        ];

        var modInstaller = InstallWithModInstaller(InstallerOf("A", null, packageFiles));

        modInstaller.InstalledFiles.Should().BeEquivalentPathTo(ToDestRootedPath(packageFiles));
        modInstaller.PackageDependencies.Should().BeEmpty();

        VerifyCallbackCalledWith(packageFiles);

        fs.AllFiles.Should().BeEquivalentPathTo(ToDestPath(packageFiles));
    }

    [Fact]
    public void AutomaticModConfigurationForVehicles()
    {
        var crdFile = Path.Combine(GameDirAtRoot, "File.crd");
        var notInGameSupportedDir = Path.Combine(GameSupportedModDirectory + "Suffix", "File");
        var drivelineRecord = $"RECORD Something{Environment.NewLine}";

        var packageFiles = new Dictionary<string, string>
        {
            [Path.Combine("SubDir", crdFile)] = "Anything",
            [Path.Combine("SubDir", notInGameSupportedDir)] = "Anything",
            ["FileAtRoot.txt"] = drivelineRecord,
        };

        var modInstaller = InstallWithModInstaller(InstallerOf("A", 0xbadcafe, packageFiles));

        string[] expectedFiles =
        {
            crdFile,
            notInGameSupportedDir,
            Path.Combine(GameSupportedModDirectory, "A_badcafe", VehicleListFile),
            Path.Combine(GameSupportedModDirectory, "A_badcafe", DrivelineFile),
            Path.Combine(GameSupportedModDirectory, "A_badcafe", "A_badcafe.xml")
        };

        modInstaller.InstalledFiles.Should().BeEquivalentPathTo(ToDestRootedPath(expectedFiles));
        modInstaller.PackageDependencies.Should().BeEmpty();

        VerifyCallbackCalledWith(expectedFiles);

        fs.AllFiles.Should().BeEquivalentPathTo(ToDestPath(expectedFiles));
        fs.GetFile(Path.Combine(destinationPath, GameSupportedModDirectory, "A_badcafe", VehicleListFile))
            .TextContents.Should().Be(crdFile);
        backupStrategyMock.Verify(m => m.AfterInstall(
            destinationRoot.Combine(GameSupportedModDirectory, "A_badcafe", VehicleListFile)));
        fs.GetFile(Path.Combine(destinationPath, GameSupportedModDirectory, "A_badcafe", DrivelineFile))
            .TextContents.Should().Be(drivelineRecord.Trim());
        backupStrategyMock.Verify(m => m.AfterInstall(
            destinationRoot.Combine(GameSupportedModDirectory, "A_badcafe", DrivelineFile)));
    }

    [Fact]
    public void AutomaticModConfigurationCanBeDisabled()
    {
        configMock.Setup(c => c.GenerateModDetails).Returns(false);

        string[] packageFiles =
        [
            Path.Combine(GameDirAtRoot, "File.crd")
        ];

        var modInstaller = InstallWithModInstaller(InstallerOf("A", null, packageFiles));

        var expectedFiles = packageFiles.Concat([
            Path.Combine(GameSupportedModDirectory, "A_0", VehicleListFile)
            // No mod xml
        ]).ToArray();

        modInstaller.InstalledFiles.Should().BeEquivalentPathTo(ToDestRootedPath(expectedFiles));
        modInstaller.PackageDependencies.Should().ContainSingle(BootfilesPackageName);

        VerifyCallbackCalledWith(expectedFiles);

        fs.AllFiles.Should().BeEquivalentPathTo(ToDestPath(expectedFiles));
    }

    [Fact]
    public void AutomaticModConfigurationNotPossibleForTracks()
    {
        string[] packageFiles =
        [
            Path.Combine(GameDirAtRoot, "File.trd")
        ];

        var modInstaller = InstallWithModInstaller(InstallerOf("Bee Cee", null, packageFiles));

        var trackListFile = Path.Combine(GameSupportedModDirectory, "BeeCee_0", TrackListFile);
        var expectedFiles = packageFiles.Concat([
            trackListFile
            // No mod xml
        ]).ToArray();

        modInstaller.InstalledFiles.Should().BeEquivalentPathTo(ToDestRootedPath(expectedFiles));
        modInstaller.PackageDependencies.Should().ContainSingle(BootfilesPackageName);

        VerifyCallbackCalledWith(expectedFiles);

        fs.AllFiles.Should().BeEquivalentPathTo(ToDestPath(expectedFiles));

        fs.GetFile(Path.Combine(destinationPath, trackListFile))
            .TextContents.Should().Be(Path.Combine(GameDirAtRoot, "@File.trd"));
    }

    [Fact]
    public void TracksAndCarsAreConfiguredOnlyWhenInstalled()
    {
        var modFile = Path.Combine(GameDirAtRoot, "Foo");
        string[] packageFiles =
        [
            Path.Combine("Subdir", modFile),
            Path.Combine("Outside.trd"),
            Path.Combine("Outside.crd"),
        ];

        var modInstaller = InstallWithModInstaller(InstallerOf("M", null, packageFiles));

        modInstaller.InstalledFiles.Should().BeEquivalentPathTo(ToDestRootedPath([modFile]));
    }

    [Fact]
    public void DeletedFilesAreNotConfigured()
    {
        string[] filesToDelete =
        [
            Path.Combine(GameDirAtRoot, "File.crd"),
            Path.Combine(GameDirAtRoot, "File.trd")
        ];

        ToDestPath(filesToDelete).ForEach(fs.AddEmptyFile);

        InstallWithModInstaller(InstallerOf("A", 1,
            filesToDelete.Select(f => f + BaseInstaller.RemoveFileSuffix)
        ));

        fs.AllFiles.Should().BeEmpty();
    }

    [Fact]
    public void ConfiguredFilesExtensionsAreCaseInsensitive()
    {
        string[] files =
        [
            Path.Combine(GameDirAtRoot, "File.CRD"),
            Path.Combine(GameDirAtRoot, "File.TRD")
        ];

        InstallWithModInstaller(InstallerOf("A", 1, files));

        fs.AllFiles.Should().BeEquivalentPathTo(ToDestPath([
            .. files,
            Path.Combine(GameSupportedModDirectory, "A_1", VehicleListFile),
            Path.Combine(GameSupportedModDirectory, "A_1", TrackListFile),
        ]));
    }

    [Fact]
    public void ReconfigurationCanBeCalledThroughSharedCapability()
    {
        var trackFile = Path.Combine(GameDirAtRoot, "Track.trd");
        var installer = InstallWithModInstaller(InstallerOf("Mod", 1, [trackFile]));

        installer.Should().BeAssignableTo<IReconfigurableInstaller>().Which
            .Reconfigure(installer.InstalledFiles, backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        fs.File.ReadAllText(Path.Combine(destinationPath, GameSupportedModDirectory, "Mod_1", TrackListFile))
            .Should().Be(Path.Combine(GameDirAtRoot, "@Track.trd"));
        installer.PackageDependencies.Should().ContainSingle(BootfilesPackageName);
    }

    [Fact]
    public void ReconfigurationDoesNotWriteUnchangedFiles()
    {
        var installer = InstallWithModInstaller(InstallerOf("Mod", 1,
            [Path.Combine(GameDirAtRoot, "Vehicle.crd")]));
        backupStrategyMock.Invocations.Clear();
        callbackMock.Invocations.Clear();

        installer.Reconfigure(installer.InstalledFiles, backupStrategyMock.Object,
            new ProcessingCallbacks<RootedPath> { Before = callbackMock.Object });

        backupStrategyMock.VerifyNoOtherCalls();
        callbackMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void ReconfigurationRemovesMissingVehiclesFromList()
    {
        var removed = Path.Combine(GameDirAtRoot, "Removed.crd");
        var remaining = Path.Combine(GameDirAtRoot, "Remaining.crd");
        var installer = InstallWithModInstaller(InstallerOf("Mod", 1, [removed, remaining]));
        fs.File.Delete(destinationRoot.Combine(removed).Full);

        installer.Reconfigure(installer.InstalledFiles, backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        fs.File.ReadAllText(destinationRoot.Combine(GameSupportedModDirectory, "Mod_1", VehicleListFile).Full)
            .Should().Be(remaining);
    }

    [Fact]
    public void ReconfigurationRemovesObsoleteTrackList()
    {
        var track = Path.Combine(GameDirAtRoot, "Track.trd");
        var installer = InstallWithModInstaller(InstallerOf("Mod", 1, [track]));
        var list = destinationRoot.Combine(GameSupportedModDirectory, "Mod_1", TrackListFile);
        fs.File.Delete(destinationRoot.Combine(track).Full);

        installer.Reconfigure(installer.InstalledFiles, backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        fs.File.Exists(list.Full).Should().BeFalse();
        installer.InstalledFiles.Should().NotContain(list);
        installer.PackageDependencies.Should().BeEmpty();
        backupStrategyMock.Verify(b => b.RestoreBackup(list), Times.Once);
    }

    [Fact]
    public void FailedListRemovalPreservesOwnership()
    {
        var track = Path.Combine(GameDirAtRoot, "Track.trd");
        var installer = InstallWithModInstaller(InstallerOf("Mod", 1, [track]));
        var list = destinationRoot.Combine(GameSupportedModDirectory, "Mod_1", TrackListFile);
        fs.File.Delete(destinationRoot.Combine(track).Full);
        backupStrategyMock.Setup(b => b.RestoreBackup(list)).Throws(new IOException("Removal failed"));

        var reconfigure = () => installer.Reconfigure(installer.InstalledFiles, backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        reconfigure.Should().Throw<IOException>().WithMessage("Removal failed");
        installer.InstalledFiles.Should().Contain(list);
    }

    [Fact]
    public void FailedListBackupPreservesOriginalContents()
    {
        var vehicle = Path.Combine(GameDirAtRoot, "Vehicle.crd");
        var list = destinationRoot.Combine(GameSupportedModDirectory, "Mod_1", VehicleListFile);
        fs.AddFile(list.Full, "Original");
        backupStrategyMock.Setup(b => b.PerformBackup(list)).Throws(new IOException("Backup failed"));
        var installer = new ModInstaller(InstallerOf("Mod", 1, [vehicle]), fs.Directory.CreateDirectory("Temp"),
            configMock.Object, destinationRoot.Directory, BootfilesPackageName);

        var install = () => installer.Install(destinationRoot.Combine, backupStrategyMock.Object,
            new ProcessingCallbacks<RootedPath>());

        install.Should().Throw<IOException>().WithMessage("Backup failed");
        fs.File.ReadAllText(list.Full).Should().Be("Original");
        installer.InstalledFiles.Should().NotContain(list);
    }

    [Fact]
    public void ReconfigurationReplacesExistingListContents()
    {
        var vehicle = Path.Combine(GameDirAtRoot, "Vehicle.crd");
        var list = destinationRoot.Combine(GameSupportedModDirectory, "Mod_1", VehicleListFile);
        fs.AddFile(list.Full, "Original\r\n");
        var installer = InstallWithModInstaller(InstallerOf("Mod", 1, [vehicle]));

        installer.Reconfigure(installer.InstalledFiles, backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        fs.File.ReadAllText(list.Full).Should().Be(vehicle);
    }

    [Fact]
    public void ReconfigurationPreservesDrivelineAfterStagingCleanup()
    {
        configMock.Setup(c => c.GenerateModDetails).Returns(false);
        var vehicle = Path.Combine(GameDirAtRoot, "Vehicle.crd");
        var installer = InstallWithModInstaller(InstallerOf("Mod", 1, new Dictionary<string, string>
        {
            [Path.Combine("PackageRoot", vehicle)] = "Vehicle",
            ["Records.txt"] = "RECORD Example"
        }));
        var driveline = destinationRoot.Combine(GameSupportedModDirectory, "Mod_1", DrivelineFile);
        fs.File.Delete(destinationRoot.Combine(vehicle).Full);
        backupStrategyMock.Invocations.Clear();

        installer.Reconfigure(installer.InstalledFiles, backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        fs.File.ReadAllText(driveline.Full).Should().Be("RECORD Example");
        installer.InstalledFiles.Should().Contain(driveline);
        installer.PackageDependencies.Should().ContainSingle(BootfilesPackageName);
        backupStrategyMock.Verify(b => b.PerformBackup(driveline), Times.Never);
        backupStrategyMock.Verify(b => b.AfterInstall(driveline), Times.Never);
    }

    [Fact]
    public void ReconfigurationCreatesManifestWhenTracksDisappear()
    {
        var track = Path.Combine(GameDirAtRoot, "Track.trd");
        var installer = InstallWithModInstaller(InstallerOf("Mod", 1,
            [track, Path.Combine(GameDirAtRoot, "Vehicle.crd")]));
        fs.File.Delete(destinationRoot.Combine(track).Full);

        installer.Reconfigure(installer.InstalledFiles, backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        fs.File.ReadAllText(destinationRoot.Combine(GameSupportedModDirectory, "Mod_1", "Mod_1.xml").Full)
            .Should().Be(installer.ModManifest);
        installer.PackageDependencies.Should().BeEmpty();
    }

    [Fact]
    public void ReconfigurationRemovesObsoleteManifest()
    {
        var vehicle = Path.Combine(GameDirAtRoot, "Vehicle.crd");
        var installer = InstallWithModInstaller(InstallerOf("Mod", 1, [vehicle]));
        var manifest = destinationRoot.Combine(GameSupportedModDirectory, "Mod_1", "Mod_1.xml");
        fs.File.Delete(destinationRoot.Combine(vehicle).Full);

        installer.Reconfigure(installer.InstalledFiles, backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        fs.File.Exists(manifest.Full).Should().BeFalse();
        installer.InstalledFiles.Should().NotContain(manifest);
    }

    [Fact]
    public void ReconfigurationRetriesFailedListUpdate()
    {
        var vehicle = Path.Combine(GameDirAtRoot, "Vehicle.crd");
        var list = destinationRoot.Combine(GameSupportedModDirectory, "Mod_1", VehicleListFile);
        fs.AddFile(list.Full, "Original\r\n");
        backupStrategyMock.SetupSequence(b => b.AfterInstall(list))
            .Throws(new IOException("Tracking failed"))
            .Pass();
        var installer = new ModInstaller(InstallerOf("Mod", 1, [vehicle]), fs.Directory.CreateDirectory("Temp"),
            configMock.Object, destinationRoot.Directory, BootfilesPackageName);
        var install = () => installer.Install(destinationRoot.Combine, backupStrategyMock.Object,
            new ProcessingCallbacks<RootedPath>());
        install.Should().Throw<IOException>();

        installer.Reconfigure(installer.InstalledFiles, backupStrategyMock.Object, new ProcessingCallbacks<RootedPath>());

        fs.File.ReadAllText(list.Full).Should().Be(vehicle);
    }

    [Fact]
    public void ConfigurationFailureLeavesModPartiallyInstalled()
    {
        var trackFile = Path.Combine(GameDirAtRoot, "Track.trd");
        var installer = new ModInstaller(InstallerOf("Mod", 1, new[] { trackFile }),
            fs.Directory.CreateDirectory("Temp"), configMock.Object, destinationRoot.Directory, BootfilesPackageName);
        var configFile = destinationRoot.Combine(GameSupportedModDirectory, "Mod_1", TrackListFile);
        callbackMock.Setup(c => c(configFile)).Throws(new IOException("Configuration failed"));

        var install = () => installer.Install(destinationRoot.Combine, backupStrategyMock.Object,
            new ProcessingCallbacks<RootedPath> { Before = callbackMock.Object });

        install.Should().Throw<IOException>().WithMessage("Configuration failed");
        installer.Installed.Should().Be(IInstallation.State.PartiallyInstalled);
        installer.InstalledFiles.Should().BeEquivalentPathTo(ToDestRootedPath(new[] { trackFile }));
    }

    #region Utility Methods

    private ModInstaller InstallWithModInstaller(IPackageInstaller inner)
    {
        var tempDir = fs.Directory.CreateDirectory("Temp");
        var modInstaller = new ModInstaller(inner, fs.DirectoryInfo.New(tempDir.FullName), configMock.Object,
            destinationRoot.Directory, BootfilesPackageName);
        modInstaller.Install(destinationRoot.Combine,
            backupStrategyMock.Object, new ProcessingCallbacks<RootedPath> { Before = callbackMock.Object });
        tempDir.Delete(recursive: true);
        return modInstaller;
    }

    private StaticFilesInstaller InstallerOf(string name, int? versionHash, IEnumerable<string> files) =>
        InstallerOf(name, versionHash, files.ToDictionary(f => f, _ => Convert.ToString(versionHash) ?? string.Empty));

    private StaticFilesInstaller InstallerOf(string name, int? versionHash,
        IReadOnlyDictionary<string, string> fileContents) =>
        new(fs, fakeTimeProvider, name, versionHash, fileContents, Array.Empty<string>());

    private List<string> ToDestPath(IReadOnlyCollection<string> relativePaths) =>
        [.. relativePaths.Select(f => Path.Combine(destinationPath, f))];

    private HashSet<RootedPath> ToDestRootedPath(IReadOnlyCollection<string> relativePaths) =>
        [.. relativePaths.Select(destinationRoot.Combine)];

    private void VerifyCallbackCalledWith(IReadOnlyCollection<string> relativePaths)
    {
        foreach (var rp in ToDestRootedPath(relativePaths))
        {
            callbackMock.Verify(a => a(rp), Times.Once);
        }

        callbackMock.VerifyNoOtherCalls();
    }

    private static string InvertCasing(string path) =>
        new(path.Select(InvertCasing).ToArray());

    private static char InvertCasing(char c) =>
        InvertedCasing.GetValueOrDefault(c, c);

    #endregion
}
