using System.IO.Abstractions.TestingHelpers;
using Core.API;
using Core.Mods;
using Core.Mods.Installation;
using Core.Mods.Installation.Installers;
using Core.Packages;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.State;
using Core.State.JsonFile;
using Core.Tests.Packages.Installation.Installers;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Core.Tests.Mods.Installation;

[IntegrationTest]
public class ModConfigurationReconciliationTest
{
    private const string BootfilesName = "__bootfiles_test";
    private const string OriginalDriveline = "Original game\r\nEND";
    private readonly MockFileSystem fs = new();
    private readonly FakeTimeProvider time = new(DateTimeOffset.UtcNow.AddDays(1));
    private readonly ModInstallConfig config = new() { GenerateModDetails = false };
    private readonly Mock<PackageReconciliationService.IEventHandler> events = new();
    private readonly ModReconciliationService<PackageReconciliationService.IEventHandler> service;
    private readonly string game = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "Game");
    private readonly string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "Temp");
    private IReadOnlyDictionary<string, PackageInstallationState> state = new Dictionary<string, PackageInstallationState>();

    private string DrivelinePath => Path.Combine(game, config.BootfilesDrivelineDir, config.DrivelineFileName);

    public ModConfigurationReconciliationTest()
    {
        var naming = new PrefixBootfilesNaming(config);
        var backups = new MoveFileBackupStrategy(fs, new BackupNaming(), null);
        var provider = new Mock<IBackupStrategyProvider<DateTimeOffset, PackageReconciliationService.IEventHandler>>();
        provider.Setup(p => p.BackupStrategy(It.IsAny<DateTimeOffset>(), It.IsAny<PackageReconciliationService.IEventHandler>()))
            .Returns((DateTimeOffset installedAt, PackageReconciliationService.IEventHandler handler) =>
                new SkipUpdatedBackupStrategy(fs, backups, installedAt, handler));
        var factory = new Mock<IModInstallerFactory<PackageReconciliationService.IEventHandler>>();
        factory.Setup(f => f.ModInstaller(It.IsAny<IPackageInstaller>(), It.IsAny<IPackageInstaller>()))
            .Returns((IPackageInstaller inner, IPackageInstaller bootfiles) =>
                new ModInstaller(fs, inner, temp, config, game, bootfiles.PackageName));
        factory.Setup(f => f.BootfilesInstaller(It.IsAny<IPackageInstaller>(),
                It.IsAny<PackageReconciliationService.IEventHandler>(), It.IsAny<PackageInstallationState>()))
            .Returns((IPackageInstaller? inner, PackageReconciliationService.IEventHandler _, PackageInstallationState? previous) =>
                new BootfilesInstaller(fs, inner, temp, config, game, naming,
                    Mock.Of<BootfilesInstaller.IEventHandler>(), previous));
        service = new(provider.Object, naming, factory.Object, fs);
        fs.AddFile(DrivelinePath, OriginalDriveline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_UsesManagedPriorityAcrossReorderingDisablingAndReapplication(bool reverseDirectoryCreation)
    {
        foreach (var name in reverseDirectoryCreation ? new[] { "B", "A" } : new[] { "A", "B" })
            fs.Directory.CreateDirectory(Path.Combine(game, config.GameSupportedModDir, ModInstaller.ConfigurationName(name, 1)));
        var untracked = Path.Combine(game, config.GameSupportedModDir, "Untracked", config.DrivelineFileName);
        fs.AddFile(untracked, "RECORD Shared\n    UntrackedOnly 99");

        Apply("A", "B");

        ConfiguredRecords().Should().BeEquivalentTo([Shared("A"), Unique("A"), Unique("B")]);
        fs.File.ReadAllText(DrivelinePath + ".orig").Should().Be(OriginalDriveline);
        ReloadState();
        events.Invocations.Clear();

        Apply("B", "A");

        ConfiguredRecords().Should().BeEquivalentTo([Shared("B"), Unique("A"), Unique("B")]);
        events.Verify(e => e.SkippingPackage("A"), Times.Once);
        events.Verify(e => e.SkippingPackage("B"), Times.Once);
        events.Verify(e => e.InstallingPackage(It.IsAny<string>()), Times.Never);
        ReloadState();

        Apply("A");

        ConfiguredRecords().Should().BeEquivalentTo([Shared("A"), Unique("A")]);
        state.Should().NotContainKey("B");
        var before = fs.AllFiles.Where(p => p.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p, p => fs.File.ReadAllText(p));
        ReloadState();
        Apply("A");
        fs.AllFiles.Where(p => p.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p, p => fs.File.ReadAllText(p)).Should().BeEquivalentTo(before);

        Apply();

        state.Should().BeEmpty();
        fs.File.ReadAllText(DrivelinePath).Should().Be(OriginalDriveline);
        fs.File.Exists(DrivelinePath + ".orig").Should().BeFalse();
        fs.File.ReadAllText(untracked).Should().Be("RECORD Shared\n    UntrackedOnly 99");
    }

    private static string Shared(string name) => $"RECORD Shared{Environment.NewLine}    Ratio 1{Environment.NewLine}    {name}Only 1";
    private static string Unique(string name) => $"RECORD {name}Unique{Environment.NewLine}    Ratio 1";

    private IReadOnlyList<string> ConfiguredRecords()
    {
        var contents = fs.File.ReadAllText(DrivelinePath);
        const string begin = "### BEGIN AMS2CM";
        var start = contents.IndexOf(begin, StringComparison.Ordinal);
        var end = contents.IndexOf("### END AMS2CM", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return DrivelineRecords.Parse(contents[(start + begin.Length)..end]);
    }

    private void Apply(params string[] modsInPriorityOrder)
    {
        IPackage Package(string name, IReadOnlyDictionary<string, string> files)
        {
            var package = new Mock<IPackage>();
            package.Setup(p => p.CreateInstaller()).Returns(() => new StaticFilesInstaller(fs, time, name, 1, files, []));
            return package.Object;
        }
        var packages = modsInPriorityOrder.Select(name => Package(name, new Dictionary<string, string>
        {
            [Path.Combine("Content", "vehicles", name + ".crd")] = name,
            ["README.txt"] = Shared(name) + Environment.NewLine + Environment.NewLine + Unique(name)
        })).Append(Package(BootfilesName, new Dictionary<string, string>
        {
            [Path.Combine(config.BootfilesDrivelineDir, config.DrivelineFileName)] = "Bootfiles base\r\nEND"
        })).ToArray();
        service.Reconcile(state, packages, game, current => state = current, events.Object, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));
    }

    private void ReloadState()
    {
        var persistence = new JsonFileStatePersistence(fs, game + ".state.json", game + ".installed.json");
        persistence.WriteState(new SavedState(state));
        state = persistence.ReadState().Installation;
    }

    private class BackupNaming : MoveFileBackupStrategy.IBackupFileNaming
    {
        public string ToBackup(string path) => path + ".orig";
        public bool IsBackup(string path) => path.EndsWith(".orig", StringComparison.OrdinalIgnoreCase);
    }
}
