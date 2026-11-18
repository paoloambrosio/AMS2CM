using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Runtime.Versioning;
using Core.Games;
using FluentAssertions;
using Newtonsoft.Json;

namespace Core.Tests.Games;

[SupportedOSPlatform("linux")]
public class LinuxSteamTest
{
    #region Setup

    private const string AppId = "1";
    private static readonly string HomeDirectory = Path.Combine(Path.GetTempPath(), "LinuxSteamTest");
    private static readonly string LibraryPath = Path.Combine(HomeDirectory, "Library");
    private static readonly string NativeRoot = Path.Combine(HomeDirectory, ".local", "share", "Steam");
    private static readonly string FlatpakRoot = Path.Combine(HomeDirectory, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
    private readonly MockFileSystem fs = new();
    private readonly LinuxSteam steam;

    public LinuxSteamTest()
    {
        fs.Directory.CreateDirectory(LibraryPath);
        steam = new LinuxSteam(fs, HomeDirectory, null);
    }

    #endregion

    [Theory]
    [InlineData(".steam/steam")]
    [InlineData(".steam/root")]
    [InlineData(".steam/debian-installation")]
    [InlineData(".local/share/Steam")]
    [InlineData(".var/app/com.valvesoftware.Steam/.local/share/Steam")]
    [InlineData(".var/app/com.valvesoftware.Steam/data/Steam")]
    [InlineData("snap/steam/common/.local/share/Steam")]
    [InlineData(".snap/data/steam/common/.local/share/Steam")]
    public void AppLibraryPath_ReadsInstallationConfiguration(string relativeRoot)
    {
        var root = Path.Combine(HomeDirectory, Path.Combine(relativeRoot.Split('/')));
        AddConfiguration(root, LibraryPath);

        var library = steam.AppLibraryPath(AppId);

        library.Should().Be(LibraryPath);
    }

    [Fact]
    public void AppLibraryPath_UsesAbsoluteDataHome()
    {
        var dataHome = Path.Combine(HomeDirectory, "Data");
        AddConfiguration(Path.Combine(dataHome, "Steam"), LibraryPath);
        var locator = new LinuxSteam(fs, HomeDirectory, dataHome);

        var library = locator.AppLibraryPath(AppId);

        library.Should().Be(LibraryPath);
    }

    [Fact]
    public void AppLibraryPath_IgnoresRelativeDataHome()
    {
        AddConfiguration(NativeRoot, LibraryPath);
        AddConfiguration(Path.Combine("RelativeData", "Steam"), Path.Combine(HomeDirectory, "Other"));
        var locator = new LinuxSteam(fs, HomeDirectory, "RelativeData");

        var library = locator.AppLibraryPath(AppId);

        library.Should().Be(LibraryPath);
    }

    [Fact]
    public void AppLibraryPath_DeduplicatesConfigurationAliases()
    {
        AddConfiguration(NativeRoot, LibraryPath);
        fs.Directory.CreateDirectory(Path.Combine(HomeDirectory, ".steam"));
        fs.Directory.CreateSymbolicLink(Path.Combine(HomeDirectory, ".steam", "steam"), NativeRoot);

        var library = steam.AppLibraryPath(AppId);

        library.Should().Be(LibraryPath);
    }

    [Fact]
    public void AppLibraryPath_DeduplicatesLibrariesThroughParentSymlinks()
    {
        var alias = Path.Combine(HomeDirectory, "Alias");
        fs.Directory.CreateSymbolicLink(alias, HomeDirectory);
        AddConfiguration(NativeRoot, LibraryPath);
        AddConfiguration(FlatpakRoot, Path.Combine(alias, "Library"));

        var library = steam.AppLibraryPath(AppId);

        library.Should().Be(LibraryPath);
    }

    [Fact]
    public void AppLibraryPath_DeduplicatesLibrarySymlinks()
    {
        var alias = Path.Combine(HomeDirectory, "Alias");
        fs.Directory.CreateSymbolicLink(alias, LibraryPath);
        AddConfiguration(NativeRoot, LibraryPath);
        AddConfiguration(FlatpakRoot, alias);

        var library = steam.AppLibraryPath(AppId);

        library.Should().Be(LibraryPath);
    }

    [Fact]
    public void AppLibraryPath_MultipleLibrariesReportsPaths()
    {
        var otherLibrary = Path.Combine(HomeDirectory, "Other");
        AddConfiguration(NativeRoot, LibraryPath);
        AddConfiguration(FlatpakRoot, otherLibrary);

        var locate = () => steam.AppLibraryPath(AppId);

        locate.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{LibraryPath}*{otherLibrary}*Game:Path*");
    }

    [Fact]
    public void AppLibraryPath_CaseDistinctLibrariesAreAmbiguous()
    {
        AddConfiguration(NativeRoot, LibraryPath);
        AddConfiguration(FlatpakRoot, Path.Combine(HomeDirectory, "library"));

        var locate = () => steam.AppLibraryPath(AppId);

        locate.Should().Throw<InvalidOperationException>().WithMessage("*multiple Steam libraries*");
    }

    [Fact]
    public void AppLibraryPath_MissingConfigurationExplainsOverride()
    {
        var locate = () => steam.AppLibraryPath(AppId);

        locate.Should().Throw<InvalidOperationException>().WithMessage("*configuration*Game:Path*");
    }

    [Fact]
    public void AppLibraryPath_MissingAppReportsAppId()
    {
        AddConfiguration(NativeRoot, LibraryPath, "2");

        var locate = () => steam.AppLibraryPath(AppId);

        locate.Should().Throw<InvalidOperationException>().WithMessage($"*app ID {AppId}*Game:Path*");
    }

    [Fact]
    public void AppLibraryPath_RelativeLibraryPathIsRejected()
    {
        AddConfiguration(NativeRoot, "Relative");

        var locate = () => steam.AppLibraryPath(AppId);

        locate.Should().Throw<InvalidDataException>().WithMessage("*Relative*absolute*");
    }

    [Fact]
    public void AppLibraryPath_InvalidConfigurationIsReported()
    {
        var configuration = ConfigurationPath(NativeRoot);
        fs.AddFile(configuration, new MockFileData("\"libraryfolders\" \"invalid\""));
        AddConfiguration(FlatpakRoot, LibraryPath);

        var locate = () => steam.AppLibraryPath(AppId);

        locate.Should().Throw<InvalidDataException>().WithMessage($"*{configuration}*");
    }

    [Fact]
    public void AppLibraryPath_InaccessibleConfigurationIsReported()
    {
        var configuration = ConfigurationPath(Path.Combine(HomeDirectory, ".steam", "steam"));
        var file = new Mock<IFile>();
        file.Setup(f => f.GetAttributes(configuration)).Throws(new UnauthorizedAccessException("Denied"));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.SetupGet(f => f.Path).Returns(fs.Path);
        fileSystem.SetupGet(f => f.File).Returns(file.Object);
        var locator = new LinuxSteam(fileSystem.Object, HomeDirectory, null);

        var locate = () => locator.AppLibraryPath(AppId);

        locate.Should().Throw<IOException>().WithMessage($"*{configuration}*")
            .WithInnerException<UnauthorizedAccessException>();
    }

    [Fact]
    public void AppLibraryPath_ConfigurationDirectoryIsRejected()
    {
        var configuration = ConfigurationPath(NativeRoot);
        fs.Directory.CreateDirectory(configuration);

        var locate = () => steam.AppLibraryPath(AppId);

        locate.Should().Throw<InvalidDataException>().WithMessage($"*{configuration}*directory*");
    }

    #region Utility Methods

    private void AddConfiguration(string root, string libraryPath, string appId = AppId) =>
        fs.AddFile(ConfigurationPath(root), new MockFileData($$"""
            "libraryfolders"
            {
                "0"
                {
                    "path" {{JsonConvert.ToString(libraryPath)}}
                    "apps" { "{{appId}}" "10" }
                }
            }
            """));

    private static string ConfigurationPath(string root) => Path.Combine(root, "steamapps", "libraryfolders.vdf");

    #endregion
}
