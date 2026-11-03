using System.IO.Abstractions.TestingHelpers;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation;
using Core.Utils;
using FluentAssertions;

namespace Core.Tests.Mods.Installation.Installers;

[UnitTest]
public class GeneratedBootfilesInstallerTest
{
    #region Setup

    private readonly MockFileSystem fs = new();
    private static readonly string[] Names = ["BOOTFLOW.bff", "BOOTSPLASH.bff", "PHYSICSPERSISTENT.bff"];
    private static readonly DateTime Timestamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public GeneratedBootfilesInstallerTest()
    {
        foreach (var name in Names)
        {
            fs.AddFile(Path.Combine("Game", "Pakfiles", name), new MockFileData(name)
            {
                LastWriteTime = Timestamp,
                CreationTime = Timestamp
            });
        }
    }

    #endregion

    [Theory]
    [InlineData("BOOTFLOW.bff")]
    [InlineData("BOOTSPLASH.bff")]
    [InlineData("PHYSICSPERSISTENT.bff")]
    public void EachSourceChangesVersion(string name)
    {
        var original = CreateInstaller().PackageVersionHash;
        fs.File.AppendAllText(Path.Combine("Game", "Pakfiles", name), "Changed");

        CreateInstaller().PackageVersionHash.Should().NotBe(original);
    }

    [Fact]
    public void VersionIsCachedBeforeMutation()
    {
        var installer = CreateInstaller();
        var original = installer.PackageVersionHash;
        fs.File.Delete(Path.Combine("Game", "Pakfiles", Names[0]));

        installer.PackageVersionHash.Should().Be(original);
    }

    [Fact]
    public void OwnedSourcesUseOriginalBackupsIndividually()
    {
        var original = CreateInstaller().PackageVersionHash;
        var relative = Path.Combine("Pakfiles", Names[0]);
        var path = Path.Combine("Game", relative);
        fs.File.Move(path, path + ".orig");
        fs.AddFile(path, new MockFileData("Installed content") { CreationTime = Timestamp });
        var installer = CreateInstaller();

        installer.SelectSources(new Dictionary<string, PackageInstallationState>
        {
            ["Bootfiles"] = new(new DateTimeOffset(Timestamp), 1, false, [], [relative], [])
        });

        installer.PackageVersionHash.Should().Be(original);
    }

    [Fact]
    public void UpdatedOwnedSourceUsesLiveFile()
    {
        var relative = Path.Combine("Pakfiles", Names[0]);
        var path = Path.Combine("Game", relative);
        fs.AddFile(path + ".orig", "Old source");
        fs.File.SetCreationTimeUtc(path, Timestamp.AddDays(1));
        var expected = CreateInstaller().PackageVersionHash;
        var installer = CreateInstaller();

        installer.SelectSources(new Dictionary<string, PackageInstallationState>
        {
            ["Bootfiles"] = new(new DateTimeOffset(Timestamp), 1, false, [], [relative], [])
        });

        installer.PackageVersionHash.Should().Be(expected);
    }

    [Fact]
    public void ConstructionDoesNotReadMissingSources()
    {
        fs.Directory.Delete("Game", recursive: true);

        var create = CreateInstaller;

        create.Should().NotThrow();
    }

    [Fact]
    public void PreparationFailureLeavesInstalledSourcesUntouched()
    {
        var path = Path.Combine("Game", "Pakfiles", Names[0]);
        var original = fs.File.ReadAllText(path);
        var installer = CreateInstaller();

        var prepare = installer.Prepare;

        prepare.Should().Throw<Exception>();
        fs.File.ReadAllText(path).Should().Be(original);
    }

    [Fact]
    public void SingleFileHashMatchesSequenceOverload()
    {
        var file = fs.FileInfo.New(Path.Combine("Game", "Pakfiles", Names[0]));

        FileSystemHash.Hash32(file).Should().Be(FileSystemHash.Hash32(new[] { file }));
    }

    #region Utility Methods

    private GeneratedBootfilesInstaller CreateInstaller() =>
        new("Generated", fs.DirectoryInfo.New("Game"), fs.Directory.CreateDirectory("Temp"));

    #endregion
}
