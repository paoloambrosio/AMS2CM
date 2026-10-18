using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Utils;
using FluentAssertions;
using static Core.Mods.Installation.Installers.GeneratedBootfilesInstaller;

namespace Core.Tests.Mods.Installation.Installers;

[UnitTest]
public class GeneratedBootfilesInstallerTest
{
    private readonly MockFileSystem fs = new();
    private static readonly string[] Sources = [BootFlowPakFileName, BootSplashPakFileName, PhysicsPersistentPakFileName];
    private static readonly DateTime Stamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset InstalledAt = new(Stamp.AddHours(1));

    [Fact]
    public void ConstructionDoesNotReadSourcesOrCreateExtractionDirectories()
    {
        var directories = fs.AllDirectories.ToArray();
        _ = NewInstaller();
        fs.AllFiles.Should().BeEmpty();
        fs.AllDirectories.Should().BeEquivalentTo(directories);
    }

    [Fact]
    public void DirectoryEnumerationCreatesOneExtractionDirectoryInTheInjectedFilesystem()
    {
        var fileSystem = new Mock<IFileSystem>();
        var directory = new Mock<IDirectoryInfo>();
        directory.Setup(d => d.EnumerateDirectories("*", It.IsAny<EnumerationOptions>())).Returns([]);
        fileSystem.Setup(f => f.Directory.CreateDirectory(It.IsAny<string>())).Returns(directory.Object);
        var installer = new GeneratedBootfilesInstaller(fileSystem.Object, "Generated", "Game", "Temp");
        fileSystem.Verify(f => f.Directory.CreateDirectory(It.IsAny<string>()), Times.Never);

        installer.RelativeDirectoryPaths.Should().BeEmpty();
        installer.RelativeDirectoryPaths.Should().BeEmpty();

        fileSystem.Verify(f => f.Directory.CreateDirectory(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void VersionIsStableAfterBootfilesInstallationAndCachedBeforeMutation()
    {
        AddSources();
        var installer = NewInstaller();
        var version = installer.PackageVersionHash;
        SimulateInstalledBootfiles();

        installer.PackageVersionHash.Should().Be(version);
        NewInstaller(InstalledAt).PackageVersionHash.Should().Be(version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void VersionDetectsEachSourceUpdateIndependentlyAndRemainsStableAfterReinstall(int changed)
    {
        AddSources();
        var version = NewInstaller().PackageVersionHash;
        SimulateInstalledBootfiles();
        fs.AddFile(SourcePath(Sources[changed]), new MockFileData("Updated " + Sources[changed])
        {
            CreationTime = InstalledAt.AddMinutes(1),
            LastWriteTime = Stamp.AddMinutes(1)
        });
        fs.File.SetCreationTimeUtc(SourcePath(Sources[changed]), InstalledAt.AddMinutes(1).UtcDateTime);

        var updatedVersion = NewInstaller(InstalledAt).PackageVersionHash;
        updatedVersion.Should().NotBe(version);

        var restore = new SkipUpdatedBackupStrategy(fs, new MoveFileBackupStrategy(fs, new BackupNaming(), null),
            InstalledAt, null);
        foreach (var name in new[] { BootFlowPakFileName, PhysicsPersistentPakFileName })
            restore.RestoreBackup(new RootedPath("Game", Path.Combine(PakfilesDirectory, name)));
        NewInstaller().PackageVersionHash.Should().Be(updatedVersion);

        var reinstalledAt = InstalledAt.AddMinutes(2);
        SimulateInstalledBootfiles(reinstalledAt);
        NewInstaller(reinstalledAt).PackageVersionHash.Should().Be(updatedVersion);
    }

    [Fact]
    public void MissingSourceReportsThePath()
    {
        AddSources();
        fs.File.Delete(SourcePath(BootSplashPakFileName));

        var readVersion = () => NewInstaller().PackageVersionHash;
        readVersion.Should().Throw<FileNotFoundException>()
            .Which.FileName.Should().Be(fs.Path.GetFullPath(SourcePath(BootSplashPakFileName)));
    }

    [Fact]
    public void MissingOriginalDoesNotHashTheManagedBootflowReplacement()
    {
        AddSources();
        SimulateInstalledBootfiles();
        fs.File.Delete(SourcePath(BootFlowPakFileName) + ".orig");

        var readVersion = () => NewInstaller(InstalledAt).PackageVersionHash;

        readVersion.Should().Throw<FileNotFoundException>().Which.FileName
            .Should().Be(fs.Path.GetFullPath(SourcePath(BootFlowPakFileName) + ".orig"));
    }

    [Theory]
    [InlineData(BootFlowPakFileName)]
    [InlineData(PhysicsPersistentPakFileName)]
    public void VersionUsesAnOriginalRestoredByAPreviousShadower(string restoredSource)
    {
        AddSources();
        var version = NewInstaller().PackageVersionHash;
        SimulateInstalledBootfiles();
        fs.File.Delete(SourcePath(restoredSource));
        fs.File.Move(SourcePath(restoredSource) + ".orig", SourcePath(restoredSource));
        var previous = new PackageInstallationState(InstalledAt, version, false, [],
            new[] { BootFlowPakFileName, PhysicsPersistentPakFileName }
                .Where(name => name != restoredSource).Select(name => Path.Combine(PakfilesDirectory, name)).ToArray(),
            ["Previous shadower"]);

        var installer = new GeneratedBootfilesInstaller(fs, "Generated", "Game", "Temp", previousInstallation: previous);

        installer.PackageVersionHash.Should().Be(version);
    }

    private GeneratedBootfilesInstaller NewInstaller(DateTimeOffset? previousInstallationTime = null) =>
        new(fs, "Generated", "Game", "Temp", previousInstallation: previousInstallationTime is { } installedAt
            ? new PackageInstallationState(installedAt, null, false, [],
                new[] { BootFlowPakFileName, PhysicsPersistentPakFileName }
                    .Select(name => Path.Combine(PakfilesDirectory, name)).ToArray(), [])
            : null);
    private static string SourcePath(string name) => Path.Combine("Game", PakfilesDirectory, name);

    private void AddSources()
    {
        foreach (var name in Sources)
            fs.AddFile(SourcePath(name), new MockFileData(name) { CreationTime = Stamp, LastWriteTime = Stamp });
    }

    private void SimulateInstalledBootfiles(DateTimeOffset? installedAt = null)
    {
        fs.File.Move(SourcePath(BootFlowPakFileName), SourcePath(BootFlowPakFileName) + ".orig");
        fs.File.Move(SourcePath(PhysicsPersistentPakFileName), SourcePath(PhysicsPersistentPakFileName) + ".orig");
        fs.AddFile(SourcePath(BootFlowPakFileName), new MockFileData("Modified bootflow")
        {
            CreationTime = installedAt ?? InstalledAt,
            LastWriteTime = Stamp
        });
    }

    private class BackupNaming : MoveFileBackupStrategy.IBackupFileNaming
    {
        public string ToBackup(string path) => path + ".orig";
        public bool IsBackup(string path) => path.EndsWith(".orig");
    }
}
