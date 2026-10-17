using System.IO.Abstractions.TestingHelpers;
using Core.Mods.Installation.Installers;
using FluentAssertions;
using static Core.Mods.Installation.Installers.GeneratedBootfilesInstaller;

namespace Core.Tests.Mods.Installation.Installers;

[UnitTest]
public class GeneratedBootfilesInstallerTest
{
    private readonly MockFileSystem fs = new();
    private static readonly string[] Sources = [BootFlowPakFileName, BootSplashPakFileName, PhysicsPersistentPakFileName];
    private static readonly DateTime Stamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ConstructionDoesNotReadSourcesOrCreateExtractionDirectories()
    {
        var directories = fs.AllDirectories.ToArray();
        _ = NewInstaller();
        fs.AllFiles.Should().BeEmpty();
        fs.AllDirectories.Should().BeEquivalentTo(directories);
    }

    [Fact]
    public void VersionIsStableAfterBootfilesInstallationAndCachedBeforeMutation()
    {
        AddSources();
        var installer = NewInstaller();
        var version = installer.PackageVersionHash;
        SimulateInstalledBootfiles();

        installer.PackageVersionHash.Should().Be(version);
        NewInstaller().PackageVersionHash.Should().Be(version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void VersionUsesLiveSourcesAfterGameUpdateInsteadOfStaleBackups(int changed)
    {
        AddSources();
        var version = NewInstaller().PackageVersionHash;
        SimulateInstalledBootfiles();
        AddSources();
        fs.File.SetLastWriteTimeUtc(SourcePath(Sources[changed]), Stamp.AddMinutes(1));

        NewInstaller().PackageVersionHash.Should().NotBe(version);
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

    private GeneratedBootfilesInstaller NewInstaller() => new(fs, "Generated", "Game", "Temp");
    private static string SourcePath(string name) => Path.Combine("Game", PakfilesDirectory, name);

    private void AddSources()
    {
        foreach (var name in Sources)
            fs.AddFile(SourcePath(name), new MockFileData(name) { LastWriteTime = Stamp });
    }

    private void SimulateInstalledBootfiles()
    {
        fs.File.Move(SourcePath(BootFlowPakFileName), SourcePath(BootFlowPakFileName) + ".orig");
        fs.File.Move(SourcePath(PhysicsPersistentPakFileName), SourcePath(PhysicsPersistentPakFileName) + ".orig");
        fs.AddFile(SourcePath(BootFlowPakFileName), "Modified bootflow");
    }
}
