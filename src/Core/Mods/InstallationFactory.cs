using Core.Games;
using Core.Mods;

namespace Core;

public class InstallationFactory : IInstallationFactory
{
    public interface IConfig : BaseInstaller.IConfig
    {
    }

    private readonly IConfig config;
    private readonly IGame game;
    private readonly ITempDir tempDir;

    public InstallationFactory(IGame game, ITempDir tempDir, IConfig config)
    {
        this.game = game;
        this.tempDir = tempDir;
        this.config = config;
    }

    // TODO move this to when the ModPackage is created!
    public IInstaller ModInstaller(ModPackage modPackage) =>
        Directory.Exists(modPackage.FullPath)
            ? new ModDirectoryInstaller(modPackage.PackageName, modPackage.FsHash, tempDir, config, modPackage.FullPath)
            : new ModArchiveInstaller(modPackage.PackageName, modPackage.FsHash, tempDir, config, modPackage.FullPath);

    // TODO the same here, but create it if there is no package!
    public IInstaller GeneratedBootfilesInstaller() =>
        new GeneratedBootfilesInstaller(tempDir, config, game);
}
