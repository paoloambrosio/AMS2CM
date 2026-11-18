using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Core.Games;
using FluentAssertions;
using Newtonsoft.Json;

namespace Core.Tests.Games;

public class SteamLibraryFoldersTest
{
    #region Setup

    private const string AppId = "1";
    private const string WindowsLibraryPath = @"C:\Library";
    private static readonly string ConfigurationPath = Path.Combine(Path.GetTempPath(), "Steam", "libraryfolders.vdf");
    private static readonly string LibraryPath = Path.Combine(Path.GetTempPath(), "Library");
    private readonly MockFileSystem fs = new();

    #endregion

    [Fact]
    public void Read_ReturnsConfiguredLibrary()
    {
        AddConfiguration($$"""
            "libraryfolders"
            {
                "0"
                {
                    "path" {{JsonConvert.ToString(LibraryPath)}}
                    "apps" { "{{AppId}}" "10" }
                }
            }
            """);

        var folders = new SteamLibraryFolders(fs).Read(ConfigurationPath);

        var folder = folders.Should().ContainSingle().Subject;
        folder.Path.Should().Be(LibraryPath);
        folder.Apps.Should().ContainKey(AppId).WhoseValue.Should().Be("10");
    }

    [Fact]
    public void Read_IgnoresMetadata()
    {
        AddConfiguration("\"libraryfolders\" { \"TimeNextStatsReport\" \"10\" }");

        var folders = new SteamLibraryFolders(fs).Read(ConfigurationPath);

        folders.Should().BeEmpty();
    }

    [Fact]
    public void Read_PreservesWindowsLibraryConfiguration()
    {
        AddConfiguration($$"""
            "LibraryFolders"
            {
                "0"
                {
                    "Path" {{JsonConvert.ToString(WindowsLibraryPath)}}
                    "Apps" { "{{AppId}}" "10" }
                }
            }
            """);

        var folders = new SteamLibraryFolders(fs).Read(ConfigurationPath);

        var folder = folders.Should().ContainSingle().Subject;
        folder.Path.Should().Be(WindowsLibraryPath);
        folder.Apps.Should().ContainKey(AppId);
    }

    [Fact]
    public void Read_MissingAppsReturnsEmptyMapping()
    {
        AddConfiguration($$"""
            "libraryfolders" { "0" { "path" {{JsonConvert.ToString(LibraryPath)}} } }
            """);

        var folders = new SteamLibraryFolders(fs).Read(ConfigurationPath);

        folders.Should().ContainSingle().Subject.Apps.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"libraryfolders\" \"value\"")]
    [InlineData("\"other\" { }")]
    [InlineData("\"libraryfolders\" { \"0\" { \"apps\" { } } }")]
    [InlineData("\"libraryfolders\" { \"0\" { \"path\" \"\" } }")]
    [InlineData("\"libraryfolders\" { \"0\" { \"path\" \"/Library\" \"apps\" \"invalid\" } }")]
    [InlineData("\"libraryfolders\" { \"0\" { \"path\" \"/Library\" } \"0\" { \"path\" \"/Other\" } }")]
    public void Read_InvalidConfigurationReportsPath(string contents)
    {
        AddConfiguration(contents);

        var read = () => new SteamLibraryFolders(fs).Read(ConfigurationPath);

        read.Should().Throw<InvalidDataException>().WithMessage($"*{ConfigurationPath}*");
    }

    [Fact]
    public void Read_UnreadableConfigurationReportsPath()
    {
        var file = new Mock<IFile>();
        file.Setup(f => f.ReadAllText(ConfigurationPath)).Throws(new UnauthorizedAccessException("Denied"));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.SetupGet(f => f.File).Returns(file.Object);

        var read = () => new SteamLibraryFolders(fileSystem.Object).Read(ConfigurationPath);

        read.Should().Throw<IOException>().WithMessage($"*{ConfigurationPath}*")
            .WithInnerException<UnauthorizedAccessException>();
    }

    #region Utility Methods

    private void AddConfiguration(string contents) => fs.AddFile(ConfigurationPath, new MockFileData(contents));

    #endregion
}
