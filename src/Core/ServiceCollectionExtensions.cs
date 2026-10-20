using Core.API;
using System.IO.Abstractions;
using Core.Games;
using Core.IO;
using Core.Mods;
using Core.Mods.Installation;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Packages.Repository;
using Core.SoftwareUpdates;
using Core.State;
using Core.State.JsonFile;
using Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Octokit;

namespace Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services, Config config)
    {
        services.TryAddSingleton(config.Game);
        services.TryAddSingleton(config.Updates);
        services.TryAddSingleton(config.ModInstall);
        services.TryAddSingleton<PrefixBootfilesNaming.IConfig>(config.ModInstall);
        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISteamLibraryLocator, Steam>();
        services.TryAddSingleton<IGame, Game>();
        services.TryAddSingleton<ITempDir>(p => new SubdirectoryTempDir(
            p.GetRequiredService<IFileSystem>(),
            Path.Combine(p.GetRequiredService<IGame>().InstallationDirectory, InstallationPaths.ModsDirectoryName)));
        services.TryAddSingleton(p =>
        {
            var gameDirectory = p.GetRequiredService<IGame>().InstallationDirectory;
            return new InstallationPaths(gameDirectory,
                Path.Combine(gameDirectory, InstallationPaths.ModsDirectoryName),
                p.GetRequiredService<ITempDir>().BasePath);
        });
        services.TryAddSingleton<IPackageInstallerFactory, PackageInstallerFactory>();
        services.TryAddSingleton<IPackageRepository>(p => new FileSystemRepository(
            p.GetRequiredService<IFileSystem>(), p.GetRequiredService<IPackageInstallerFactory>(),
            p.GetRequiredService<InstallationPaths>().ModsDirectory));
        services.TryAddSingleton<IStatePersistence>(p => new JsonFileStatePersistence(
            p.GetRequiredService<IFileSystem>(), p.GetRequiredService<InstallationPaths>().ModsDirectory));
        services.TryAddSingleton<ISafeFileDelete, WindowsRecyclingBin>();
        services.TryAddSingleton<IBootfilesNaming, PrefixBootfilesNaming>();
        services.TryAddSingleton<IRootFinder>(p =>
            new ContainedDirsRootFinder(p.GetRequiredService<ModInstallConfig>().DirsAtRoot));
        services.TryAddSingleton<IBootfilesSourceFactory, BootfilesSourceFactory>();
        services.TryAddSingleton<IModInstallerFactory<IEventHandler>, ModInstallerFactory<ModInstallConfig>>();
        services.TryAddSingleton<SuffixBackupStrategy.Provider<DateTimeOffset, IEventHandler>>();
        services.TryAddSingleton<IBackupStrategyProvider<DateTimeOffset, IEventHandler>>(p =>
            new SkipUpdatedBackupStrategy.Provider<IEventHandler>(
                p.GetRequiredService<IFileSystem>(),
                p.GetRequiredService<SuffixBackupStrategy.Provider<DateTimeOffset, IEventHandler>>()));
        services.TryAddSingleton<IReconciliationService<IEventHandler>, ModReconciliationService<IEventHandler>>();
        services.TryAddSingleton<IModStateReader, ModStateReader>();
        services.TryAddSingleton<IModPackageOperations, ModPackageOperations>();
        services.TryAddSingleton<IModSynchronizationService, ModSynchronizationService>();
        services.TryAddSingleton<IModManager, ModManager>();
        services.TryAddSingleton<IGitHubClient>(p => new GitHubClient(
            new ProductHeaderValue(p.GetRequiredService<GitHubUpdateChecker.IConfig>().GitHubClientApp)));
        services.TryAddSingleton<IUpdateChecker, GitHubUpdateChecker>();
        return services;
    }
}
