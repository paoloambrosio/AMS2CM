using System.IO.Abstractions.TestingHelpers;
using Core.API;
using Core.Games;
using Core.Mods;
using Core.Mods.Installation;
using Core.Packages;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Tests.Packages.Installation.Installers;
using Core.Utils;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Core.Tests.Mods.Installation;

[IntegrationTest]
public class ModReconfigurationTest
{
    #region Setup

    private const string Mod1 = "Mod1";
    private const string Mod2 = "Mod2";
    private const string Bootfiles = "__bootfiles";
    private static readonly string SharedVehicle = Path.Combine("vehicles", "foo.crd");
    private static readonly string OtherVehicle = Path.Combine("vehicles", "bar.crd");
    private static readonly string SharedTrack = Path.Combine("tracks", "foo.trd");
    private static readonly string OtherTrack = Path.Combine("tracks", "bar.trd");
    private static readonly string Sentinel = Path.Combine("vehicles", "sentinel");

    private readonly MockFileSystem fs = new();
    private readonly FakeTimeProvider time = new(DateTimeOffset.UtcNow.AddDays(10));
    private readonly ModInstallConfig config = new()
    {
        DirsAtRoot = ["vehicles", "tracks", "userdata"],
        GenerateModDetails = false
    };
    private readonly Mock<IEvents> events = new();
    private readonly Mock<IBackupStrategy> backups = new();
    private readonly Mock<IBackupStrategyProvider<DateTimeOffset, IEvents>> provider = new();
    private readonly PathRoot gameRoot;
    private readonly SubdirectoryTempDir temp;
    private readonly ReconciliationService<IEvents> service;
    private IReadOnlyDictionary<string, PackageInstallationState> state = new Dictionary<string, PackageInstallationState>();

    public ModReconfigurationTest()
    {
        gameRoot = new PathRoot(fs.Directory.CreateDirectory("Game"));
        temp = new SubdirectoryTempDir(fs.Directory.CreateDirectory("Workspace"));
        var game = new Mock<IGame>();
        game.SetupGet(g => g.InstallationDirectory).Returns(gameRoot.Directory);
        var naming = new Mock<IBootfilesNaming>();
        naming.Setup(n => n.IsBootfiles(Bootfiles)).Returns(true);
        var realBackups = new SuffixBackupStrategy(fs, events.Object);
        backups.Setup(b => b.PerformBackup(It.IsAny<RootedPath>())).Callback<RootedPath>(realBackups.PerformBackup);
        backups.Setup(b => b.RestoreBackup(It.IsAny<RootedPath>())).Callback<RootedPath>(realBackups.RestoreBackup);
        backups.Setup(b => b.DeleteBackup(It.IsAny<RootedPath>())).Callback<RootedPath>(realBackups.DeleteBackup);
        backups.Setup(b => b.AfterInstall(It.IsAny<RootedPath>())).Callback<RootedPath>(realBackups.AfterInstall);
        provider.Setup(p => p.BackupStrategy(It.IsAny<DateTimeOffset>(), It.IsAny<IEvents>())).Returns(backups.Object);
        service = new ReconciliationService<IEvents>(provider.Object,
            ModReconciliationContext<IEvents, ModInstallConfig>.Factory(game.Object, temp, naming.Object, config));
    }

    #endregion

    [Fact]
    public void HigherPriorityModRemovesShadowedVehicleFromRetainedConfiguration()
    {
        Reconcile(Package(Mod1, SharedVehicle, OtherVehicle));
        var original = state[Mod1];
        events.Invocations.Clear();

        Reconcile(Package(Mod2, SharedVehicle), Package(Mod1, SharedVehicle, OtherVehicle));

        ReadModFile(Mod1, config.VehicleListFileName).Should().Be(OtherVehicle);
        ReadModFile(Mod2, config.VehicleListFileName).Should().Be(SharedVehicle);
        fs.File.ReadAllText(gameRoot.Combine(SharedVehicle).Full).Should().Be(Mod2);
        state[Mod1].Files.Should().NotContain(SharedVehicle);
        state[Mod1].Time.Should().Be(original.Time);
        state[Mod1].VersionHash.Should().Be(original.VersionHash);
        state[Mod1].ShadowedBy.Should().Contain(Mod2);
        events.Verify(e => e.InstallingPackage(Mod1), Times.Never);
    }

    [Fact]
    public void RemovingShadowingModRestoresVehicleConfiguration()
    {
        Reconcile(Package(Mod1, SharedVehicle, OtherVehicle));
        Reconcile(Package(Mod2, SharedVehicle), Package(Mod1, SharedVehicle, OtherVehicle));

        Reconcile(Package(Mod1, SharedVehicle, OtherVehicle));

        ReadModFile(Mod1, config.VehicleListFileName).Split(Environment.NewLine)
            .Should().BeEquivalentTo(SharedVehicle, OtherVehicle);
        fs.File.ReadAllText(gameRoot.Combine(SharedVehicle).Full).Should().Be(Mod1);
        state[Mod1].Files.Should().Contain(SharedVehicle);
        state[Mod1].ShadowedBy.Should().BeEmpty();
    }

    [Fact]
    public void ShadowingLastTrackSwitchesRetainedVehicleModToManifest()
    {
        config.GenerateModDetails = true;
        Reconcile(Package(Mod1, SharedTrack, OtherVehicle));

        Reconcile(Package(Mod2, SharedTrack), Package(Mod1, SharedTrack, OtherVehicle));

        fs.File.Exists(ModFile(Mod1, config.TrackListFileName).Full).Should().BeFalse();
        state[Mod1].Files.Should().NotContain(ModFile(Mod1, config.TrackListFileName).Relative);
        fs.File.Exists(ModFile(Mod1, "Mod1_1.xml").Full).Should().BeTrue();
        state[Mod1].Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void RemovingTrackShadowRestoresBootfilesDependency()
    {
        config.GenerateModDetails = true;
        Reconcile(Package(Mod1, SharedTrack, OtherVehicle));
        Reconcile(Package(Mod2, SharedTrack), Package(Mod1, SharedTrack, OtherVehicle));

        Reconcile(Package(Mod1, SharedTrack, OtherVehicle));

        ReadModFile(Mod1, config.TrackListFileName).Should().Be(Path.Combine("tracks", "@foo.trd"));
        fs.File.Exists(ModFile(Mod1, "Mod1_1.xml").Full).Should().BeFalse();
        state[Mod1].Dependencies.Should().ContainSingle(Bootfiles);
    }

    [Fact]
    public void BootfilesUseRefreshedListsInSameReconciliation()
    {
        Reconcile(Package(Mod1, SharedTrack, OtherTrack));

        Reconcile(Package(Mod2, SharedTrack), Package(Mod1, SharedTrack, OtherTrack));

        var bootfilesList = fs.File.ReadAllLines(gameRoot.Combine(config.BootfilesTrackListDir, config.TrackListFileName).Full);
        bootfilesList.Count(line => line == Path.Combine("tracks", "@foo.trd")).Should().Be(1);
        bootfilesList.Should().Contain(Path.Combine("tracks", "@bar.trd"));
    }

    [Fact]
    public void UnchangedRetainedModsDoNotWriteFiles()
    {
        Reconcile(Package(Mod1, SharedVehicle));
        backups.Invocations.Clear();
        events.Invocations.Clear();

        Reconcile(Package(Mod1, SharedVehicle));

        backups.VerifyNoOtherCalls();
        events.Verify(e => e.InstallingPackage(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void RetainedReconfigurationPreservesDrivelineAfterStagingCleanup()
    {
        var files = new Dictionary<string, string>
        {
            [Path.Combine("Payload", SharedVehicle)] = Mod1,
            ["Records.txt"] = "RECORD Example"
        };
        Reconcile(Package(Mod1, files));
        backups.Invocations.Clear();

        Reconcile(Package(Mod1, files));

        ReadModFile(Mod1, config.DrivelineFileName).Should().Be("RECORD Example");
        state[Mod1].Dependencies.Should().ContainSingle(Bootfiles);
        backups.Verify(b => b.AfterInstall(ModFile(Mod1, config.DrivelineFileName)), Times.Never);
    }

    [Fact]
    public void ShadowingLastVehicleRemovesObsoleteConfigurationOwnership()
    {
        config.GenerateModDetails = true;
        Reconcile(Package(Mod1, SharedVehicle, Sentinel));

        Reconcile(Package(Mod2, SharedVehicle), Package(Mod1, SharedVehicle, Sentinel));

        state[Mod1].Files.Should().Equal(Sentinel);
        fs.File.Exists(ModFile(Mod1, config.VehicleListFileName).Full).Should().BeFalse();
        fs.File.Exists(ModFile(Mod1, "Mod1_1.xml").Full).Should().BeFalse();
    }

    [Fact]
    public void RetainedModCannotOverwriteConfigurationClaimedByAnotherPackage()
    {
        Reconcile(Package(Mod1, SharedVehicle, OtherVehicle));
        var list = ModFile(Mod1, config.VehicleListFileName);

        Reconcile(Package(Mod2, list.Relative), Package(Mod1, SharedVehicle, OtherVehicle));

        fs.File.ReadAllText(list.Full).Should().Be(Mod2);
        state[Mod1].Files.Should().NotContain(list.Relative);
        state[Mod2].Files.Should().Contain(list.Relative);
    }

    [Fact]
    public void FailedRetainedReconfigurationPersistsPartialOwnership()
    {
        Reconcile(Package(Mod1, SharedVehicle, OtherVehicle));
        backups.Setup(b => b.AfterInstall(ModFile(Mod1, config.VehicleListFileName)))
            .Throws(new IOException("Tracking failed"));

        var reconcile = () => Reconcile(Package(Mod2, SharedVehicle), Package(Mod1, SharedVehicle, OtherVehicle));

        reconcile.Should().Throw<IOException>().WithMessage("Tracking failed");
        state[Mod1].Partial.Should().BeTrue();
        state[Mod1].Dependencies.Should().ContainSingle(Bootfiles);
        state[Mod1].Files.Should().NotContain(SharedVehicle);
        state[Mod1].Files.Should().Contain(ModFile(Mod1, config.VehicleListFileName).Relative);
        ReadModFile(Mod1, config.VehicleListFileName).Should().Be(OtherVehicle);
    }

    [Fact]
    public void BackupStrategyCreationFailurePreservesRetainedOwnership()
    {
        Reconcile(Package(Mod1, SharedVehicle));
        var original = state[Mod1];
        provider.SetupSequence(p => p.BackupStrategy(It.IsAny<DateTimeOffset>(), It.IsAny<IEvents>()))
            .Returns(backups.Object)
            .Throws(new IOException("Strategy failed"));

        var reconcile = () => Reconcile(Package(Mod1, SharedVehicle));

        reconcile.Should().Throw<IOException>().WithMessage("Strategy failed");
        state[Mod1].Should().BeEquivalentTo(original with { Partial = true });
    }

    [Fact]
    public void CancellationDuringKeepStillCompletesModReconfiguration()
    {
        Reconcile(Package(Mod1, SharedVehicle, OtherVehicle));
        using var cancellation = new CancellationTokenSource();
        events.Setup(e => e.SkippingPackage(Mod1)).Callback(cancellation.Cancel);

        Reconcile([Package(Mod2, SharedVehicle), Package(Mod1, SharedVehicle, OtherVehicle)], cancellation.Token);

        ReadModFile(Mod1, config.VehicleListFileName).Should().Be(OtherVehicle);
        state[Mod1].Partial.Should().BeFalse();
    }

    [Fact]
    public void RetainedPackageSuppliedConfigurationIsPreserved()
    {
        var list = Path.Combine(config.GameSupportedModDir, "Authored", config.VehicleListFileName);
        var files = new Dictionary<string, string> { [SharedVehicle] = Mod1, [list] = "Authored content" };
        Reconcile(Package(Mod1, files));

        Reconcile(Package(Mod1, files));

        fs.File.ReadAllText(gameRoot.Combine(list).Full).Should().Be("Authored content");
        fs.File.Exists(ModFile(Mod1, config.VehicleListFileName).Full).Should().BeFalse();
        state[Mod1].Dependencies.Should().BeEmpty();
    }

    #region Utility Methods

    public interface IEvents : ReconciliationService.IEventHandler, ModReconciliationContext.IEventHandler;

    private RootedPath ModFile(string name, string fileName) =>
        gameRoot.Combine(config.GameSupportedModDir, $"{name}_1", fileName);

    private string ReadModFile(string name, string fileName) => fs.File.ReadAllText(ModFile(name, fileName).Full);

    private IPackage Package(string name, params string[] files) =>
        Package(name, files.ToDictionary(file => file, _ => name));

    private IPackage Package(string name, IReadOnlyDictionary<string, string> files)
    {
        var package = new Mock<IPackage>();
        package.Setup(p => p.CreateInstaller()).Returns(() => new StaticFilesInstaller(fs, time, name, 1, files, []));
        return package.Object;
    }

    private void Reconcile(params IPackage[] packages) => Reconcile(packages, CancellationToken.None);

    private void Reconcile(IPackage[] packages, CancellationToken cancellation)
    {
        var bootfiles = Package(Bootfiles, new Dictionary<string, string>
        {
            [Path.Combine(config.BootfilesVehicleListDir, config.VehicleListFileName)] = "",
            [Path.Combine(config.BootfilesTrackListDir, config.TrackListFileName)] = "",
            [Path.Combine(config.BootfilesDrivelineDir, config.DrivelineFileName)] = "END"
        });
        try
        {
            service.Reconcile(state, packages.Append(bootfiles), gameRoot.Directory,
                updated => state = updated, events.Object, cancellation);
        }
        finally
        {
            temp.Cleanup();
        }
    }

    #endregion
}
