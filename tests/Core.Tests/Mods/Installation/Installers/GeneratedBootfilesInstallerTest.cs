using System.IO.Abstractions.TestingHelpers;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation;
using FluentAssertions;

namespace Core.Tests.Mods.Installation.Installers;

[UnitTest]
public class GeneratedBootfilesInstallerTest
{
    private readonly MockFileSystem fs = new();
    private readonly string gameDir = Path.GetFullPath("Game");
    private static readonly DateTimeOffset InstallTime = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] Archives =
    [
        GeneratedBootfilesInstaller.BootFlowPakFileName,
        GeneratedBootfilesInstaller.BootSplashPakFileName,
        GeneratedBootfilesInstaller.PhysicsPersistentPakFileName
    ];

    public GeneratedBootfilesInstallerTest()
    {
        foreach (var name in Archives)
        {
            fs.AddFile(ArchivePath(name),
                new MockFileData(name)
                {
                    CreationTime = InstallTime.AddDays(-1), LastWriteTime = InstallTime.AddDays(-1)
                });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VersionHash_IsUnchangedAfterInstallingGeneratedBootfiles(bool differentPathCase)
    {
        var originalHash = GeneratedBootfilesInstaller.VersionHash(fs, gameDir, null);
        SimulateInstallation();

        var state = InstalledState();
        if (differentPathCase)
        {
            state = state with { Files = state.Files.Select(path => path.ToLowerInvariant()).ToArray() };
        }

        var hash = GeneratedBootfilesInstaller.VersionHash(fs, gameDir, state);

        originalHash.Should().NotBeNull();
        hash.Should().Be(originalHash);
        GeneratedBootfilesInstaller.VersionHash(fs, gameDir, InstalledState()).Should().Be(hash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void VersionHash_ChangesWhenSteamReplacesAnInput(int archiveIndex)
    {
        var originalHash = GeneratedBootfilesInstaller.VersionHash(fs, gameDir, null);
        SimulateInstallation();
        fs.AddFile(ArchivePath(Archives[archiveIndex]),
            new MockFileData("Updated game archive")
            {
                CreationTime = InstallTime.AddDays(1), LastWriteTime = InstallTime.AddDays(1)
            });
        fs.File.SetCreationTimeUtc(ArchivePath(Archives[archiveIndex]), InstallTime.AddDays(1).UtcDateTime);

        GeneratedBootfilesInstaller.VersionHash(fs, gameDir, InstalledState()).Should().NotBe(originalHash);
    }

    [Fact]
    public void VersionHash_IsUnknownWhenAnOriginalInputIsMissing()
    {
        fs.File.Delete(ArchivePath(Archives[0]));

        GeneratedBootfilesInstaller.VersionHash(fs, gameDir, null).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void VersionHash_UsesLiveArchivesWhenPhysicsArchiveReturnsEvenWithOldCreationTimes(int changedArchive)
    {
        var originalHash = GeneratedBootfilesInstaller.VersionHash(fs, gameDir, null);
        SimulateInstallation();
        foreach (var index in new[] { 0, 2 })
        {
            var path = ArchivePath(Archives[index]);
            fs.File.WriteAllText(path, index == changedArchive ? "Updated game archive" : Archives[index]);
            fs.File.SetCreationTimeUtc(path, InstallTime.AddDays(-1).UtcDateTime);
            fs.File.SetLastWriteTimeUtc(path, InstallTime.AddDays(-1).UtcDateTime);
        }

        var liveHash = GeneratedBootfilesInstaller.VersionHash(fs, gameDir, null);
        liveHash.Should().NotBe(originalHash);
        GeneratedBootfilesInstaller.VersionHash(fs, gameDir, InstalledState()).Should().Be(liveHash);
    }

    [Fact]
    public void VersionHash_DoesNotFallBackToOldBackupsWhenRestoredGameArchivesAreIncomplete()
    {
        SimulateInstallation();
        fs.File.Copy(ArchivePath(Archives[2]) + ".orig", ArchivePath(Archives[2]));
        fs.File.Delete(ArchivePath(Archives[0]));

        GeneratedBootfilesInstaller.VersionHash(fs, gameDir, InstalledState()).Should().BeNull();
    }

    private void SimulateInstallation()
    {
        foreach (var name in new[] { Archives[0], Archives[2] })
        {
            fs.File.Move(ArchivePath(name), ArchivePath(name) + ".orig");
        }

        fs.AddFile(ArchivePath(Archives[0]),
            new MockFileData("Replacement bootflow") { CreationTime = InstallTime, LastWriteTime = InstallTime });
    }

    private string ArchivePath(string name) =>
        Path.Combine(gameDir, GeneratedBootfilesInstaller.PakfilesDirectory, name);

    private static PackageInstallationState InstalledState() => new(
        Time: InstallTime, VersionHash: 1, Partial: false, Dependencies: [], ShadowedBy: [],
        Files: new[] { Archives[0], Archives[2] }
            .Select(name => Path.Combine(GeneratedBootfilesInstaller.PakfilesDirectory, name)).ToArray());
}
