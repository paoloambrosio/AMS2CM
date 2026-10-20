using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using Core.API;
using Core.Games;
using Core.Mods;
using Core.Mods.Installation;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation.Installers;
using Core.Packages.Repository;
using Core.SoftwareUpdates;
using Core.State;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Octokit;

namespace Core.Tests.API;

[IntegrationTest]
public class ServiceCollectionExtensionsTest
{
    private const string GameDirectory = @"C:\AMS2CM-DI-Tests\Game";
    private const string RelativeFile = @"vehicles\paint.dat";
    private readonly MockFileSystem fileSystem = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly Mock<ISteamLibraryLocator> steam = new(MockBehavior.Strict);
    private readonly Mock<IGitHubClient> gitHub = new(MockBehavior.Strict);
    private readonly Config config = new()
    {
        Game = new GameConfig { Path = GameDirectory, ProcessName = "AMS2CM-DI-Test-Game" }
    };

    public ServiceCollectionExtensionsTest()
    {
        fileSystem.Directory.CreateDirectory(GameDirectory);
    }

    private ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fileSystem);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(steam.Object);
        services.AddSingleton(gitHub.Object);
        services.AddCoreServices(config);
        return services;
    }

    private static ServiceProvider Build(ServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    [Fact]
    public void RegistrationResolvesTheGraphWithReplacedInfrastructure()
    {
        var services = Services();
        using var provider = Build(services);
        foreach (var service in services)
        {
            provider.GetRequiredService(service.ServiceType).Should().NotBeNull();
        }
        provider.GetRequiredService<IFileSystem>().Should().BeSameAs(fileSystem);
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(clock);
        provider.GetRequiredService<IGitHubClient>().Should().BeSameAs(gitHub.Object);
        provider.GetRequiredService<IGame>().InstallationDirectory.Should().Be(GameDirectory);
        provider.GetRequiredService<IModManager>().Should().BeSameAs(provider.GetRequiredService<IModManager>());
        steam.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepositoryInstallersUseRegisteredFileSystemAndClockThroughoutReconciliation(bool archive)
    {
        AddPackage(archive);
        var destination = Path.Combine(GameDirectory, RelativeFile);
        fileSystem.AddFile(destination, new MockFileData("original"));
        using var provider = Build(Services());
        var package = provider.GetRequiredService<IPackageRepository>().ListEnabled().Single();
        var first = package.CreateInstaller();
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = package.CreateInstaller();
        second.Should().NotBeSameAs(first);
        second.InstallTime.Should().Be(clock.GetUtcNow());
        second.Installed.Should().Be(IInstallation.State.NotInstalled);

        var manager = provider.GetRequiredService<IModManager>();
        var events = Mock.Of<IEventHandler>();
        manager.InstallEnabledMods(events);
        manager.InstallEnabledMods(events);

        fileSystem.File.ReadAllText(destination).Should().Be("modified");
        fileSystem.File.ReadAllText(destination + ".orig").Should().Be("original");
        var persistence = provider.GetRequiredService<IStatePersistence>();
        persistence.ReadState().Installation[package.Name].Time.Should().Be(clock.GetUtcNow());
        fileSystem.Directory.Exists(provider.GetRequiredService<InstallationPaths>().TempDirectory).Should().BeFalse();

        manager.UninstallAllMods(events);
        fileSystem.File.ReadAllText(destination).Should().Be("original");
        fileSystem.File.Exists(destination + ".orig").Should().BeFalse();
        persistence.ReadState().Installation.Should().BeEmpty();
    }

    [Fact]
    public void CancelledOperationDoesNotRetainItsTokenForTheNextOperation()
    {
        AddPackage(archive: false);
        using var provider = Build(Services());
        var manager = provider.GetRequiredService<IModManager>();
        var events = Mock.Of<IEventHandler>();
        manager.InstallEnabledMods(events, new CancellationToken(canceled: true));
        fileSystem.File.Exists(Path.Combine(GameDirectory, RelativeFile)).Should().BeFalse();
        provider.GetRequiredService<IStatePersistence>().ReadState().Installation.Should().BeEmpty();

        manager.InstallEnabledMods(events);
        fileSystem.File.ReadAllText(Path.Combine(GameDirectory, RelativeFile)).Should().Be("modified");
    }

    [Theory]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    public void DirectoryInstallationSkipsHiddenAndSystemFilesAndDirectories(FileAttributes attributes)
    {
        AddPackage(archive: false);
        var source = Path.Combine(GameDirectory, "Mods", "Enabled", "Paint", "vehicles");
        var excludedFile = Path.Combine(source, "excluded.dat");
        var excludedDirectory = Path.Combine(source, "excluded");
        fileSystem.AddFile(excludedFile, new MockFileData("excluded"));
        fileSystem.AddFile(Path.Combine(excludedDirectory, "nested.dat"), new MockFileData("excluded"));
        fileSystem.File.SetAttributes(excludedFile, attributes);
        fileSystem.File.SetAttributes(excludedDirectory, attributes);
        using var provider = Build(Services());
        var package = provider.GetRequiredService<IPackageRepository>().ListEnabled().Single();
        package.CreateInstaller().RelativeDirectoryPaths.Should().Equal("vehicles");

        provider.GetRequiredService<IModManager>().InstallEnabledMods(Mock.Of<IEventHandler>());

        fileSystem.File.ReadAllText(Path.Combine(GameDirectory, RelativeFile)).Should().Be("modified");
        fileSystem.File.Exists(Path.Combine(GameDirectory, "vehicles", "excluded.dat")).Should().BeFalse();
        fileSystem.Directory.Exists(Path.Combine(GameDirectory, "vehicles", "excluded")).Should().BeFalse();
    }

    [Fact]
    public void RunningGameIsRejectedBeforeInstallation()
    {
        AddPackage(archive: false);
        var services = Services();
        var game = new Mock<IGame>();
        game.SetupGet(g => g.InstallationDirectory).Returns(GameDirectory);
        game.SetupGet(g => g.IsRunning).Returns(true);
        services.AddSingleton(game.Object);
        using var provider = Build(services);
        var install = () => provider.GetRequiredService<IModManager>().InstallEnabledMods(Mock.Of<IEventHandler>());
        install.Should().Throw<Exception>().WithMessage("The game is running.");
        fileSystem.File.Exists(Path.Combine(GameDirectory, RelativeFile)).Should().BeFalse();
        fileSystem.File.Exists(Path.Combine(GameDirectory, "Mods", "state.json")).Should().BeFalse();
    }

    [Fact]
    public void GeneratedBootfilesUseFreshInstallersAndRegisteredInfrastructure()
    {
        using var provider = Build(Services());
        var factory = provider.GetRequiredService<IBootfilesSourceFactory>();
        var first = factory.Create(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        var second = factory.Create(null);

        second.Installer.Should().NotBeSameAs(first.Installer);
        second.Installer.InstallTime.Should().Be(clock.GetUtcNow());
        second.Installer.PackageName.Should().Be(provider.GetRequiredService<IBootfilesNaming>().GeneratedBootfilesName);
        second.EventPackageName.Should().Be(second.Installer.PackageName);
        var paths = provider.GetRequiredService<InstallationPaths>();
        fileSystem.Directory.GetDirectories(paths.TempDirectory).Should().HaveCount(2);
    }

    [Fact]
    public void SuppliedBootfilesAreUsedWithoutGeneratingAnotherPackage()
    {
        using var provider = Build(Services());
        var package = new Mock<IPackageInstaller>();
        package.SetupGet(p => p.PackageName).Returns("__bootfiles_supplied");
        var source = provider.GetRequiredService<IBootfilesSourceFactory>().Create(package.Object);
        source.Installer.Should().BeSameAs(package.Object);
        source.EventPackageName.Should().BeNull();
        fileSystem.Directory.Exists(provider.GetRequiredService<InstallationPaths>().TempDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateCheckingUsesRegisteredClientAndHandlesFailure()
    {
        gitHub.Setup(c => c.Repository.Release.GetLatest(config.Updates.GitHubOwner, config.Updates.GitHubRepo))
            .ThrowsAsync(new InvalidOperationException("Unavailable"));
        using var provider = Build(Services());
        (await provider.GetRequiredService<IUpdateChecker>().CheckUpdateAvailable()).Should().BeFalse();
        gitHub.Verify(c => c.Repository.Release.GetLatest(config.Updates.GitHubOwner, config.Updates.GitHubRepo), Times.Once);
    }

    [Fact]
    public void RelativeGamePathUsesInjectedSteamLocator()
    {
        config.Game = new GameConfig { Path = "Game", SteamId = "test-game" };
        steam.Setup(s => s.AppLibraryPath("test-game")).Returns(Path.GetDirectoryName(GameDirectory)!);
        using var provider = Build(Services());
        provider.GetRequiredService<IGame>().InstallationDirectory.Should().Be(GameDirectory);
        steam.Verify(s => s.AppLibraryPath("test-game"), Times.Once);
    }

    [Fact]
    public void MissingGameDirectoryStillReportsTheStartupError()
    {
        config.Game = new GameConfig { Path = @"C:\AMS2CM-DI-Tests\Missing" };
        using var provider = Build(Services());
        var resolve = () => provider.GetRequiredService<IModManager>();
        resolve.Should().Throw<Exception>().WithMessage("The game directory does not exist");
    }

    private void AddPackage(bool archive)
    {
        var enabled = Path.Combine(GameDirectory, "Mods", "Enabled");
        if (!archive)
        {
            fileSystem.AddFile(Path.Combine(enabled, "Paint", RelativeFile), new MockFileData("modified"));
            return;
        }
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(RelativeFile.Replace('\\', '/')).Open());
            writer.Write("modified");
        }
        fileSystem.AddFile(Path.Combine(enabled, "Paint.zip"), new MockFileData(buffer.ToArray()));
    }
}
