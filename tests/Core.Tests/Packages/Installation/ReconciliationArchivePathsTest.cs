using System.IO.Abstractions;
using System.IO.Compression;
using Core.API;
using Core.Mods.Installation.Installers;
using Core.Packages;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Tests.Base;
using Core.Utils;
using FluentAssertions;

namespace Core.Tests.Packages.Installation;

[IntegrationTest]
public class ReconciliationArchivePathsTest : AbstractFilesystemTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_ArchivePathAliasesShareOneOwner(bool uninstall)
    {
        var gameDir = TestDir.CreateSubdirectory("Game").FullName;
        var target = Path.Combine(gameDir, "vehicles", "Car.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "Original");
        File.SetCreationTimeUtc(target, DateTime.UtcNow.AddDays(-1));
        CreateArchive("A", "vehicles/Car.txt");
        CreateArchive("B", "vehicles/./Car.txt");
        IReadOnlyDictionary<string, PackageInstallationState> state = new Dictionary<string, PackageInstallationState>();
        var events = new Mock<PackageReconciliationService.IEventHandler>();
        var provider = new Mock<IBackupStrategyProvider<DateTimeOffset, PackageReconciliationService.IEventHandler>>();
        provider.Setup(p => p.BackupStrategy(It.IsAny<DateTimeOffset>(), It.IsAny<PackageReconciliationService.IEventHandler>()))
            .Returns((DateTimeOffset installedAt, PackageReconciliationService.IEventHandler handler) =>
                new SkipUpdatedBackupStrategy(new FileSystem(), new MoveFileBackupStrategy(new Naming(), handler), installedAt, handler));
        var service = new PackageReconciliationService<PackageReconciliationService.IEventHandler>(provider.Object);
        var config = new ModInstallConfig();
        IPackage Package(string name)
        {
            var package = new Mock<IPackage>();
            package.Setup(p => p.CreateInstaller()).Returns(() => new ModInstaller(new FileSystem(),
                new ArchiveInstaller(name, 1, TestPath(name + ".zip").Full), TestPath("Temp").Full,
                config, gameDir, "Bootfiles"));
            return package.Object;
        }

        service.Reconcile(state, [Package("A"), Package("B")], gameDir,
            current => state = current, events.Object, CancellationToken.None);

        if (uninstall)
        {
            service.Reconcile(state, [], gameDir, current => state = current, events.Object, CancellationToken.None);
            File.Exists(target).Should().BeTrue("two spellings of one path must not cause the restored original to be deleted");
            File.ReadAllText(target).Should().Be("Original");
        }
        else
        {
            File.ReadAllText(target).Should().Be("A", "the earlier package has priority even when the lower-priority archive uses a dot segment");
        }
    }

    private void CreateArchive(string name, string entryName)
    {
        using var archive = ZipFile.Open(TestPath(name + ".zip").Full, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
        writer.Write(name);
    }

    private class Naming : MoveFileBackupStrategy.IBackupFileNaming
    {
        public string ToBackup(string path) => path + ".orig";
        public bool IsBackup(string path) => path.EndsWith(".orig");
    }
}