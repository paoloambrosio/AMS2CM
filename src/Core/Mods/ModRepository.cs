using System.Collections.Immutable;

namespace Core.Mods;

public class ModRepository : IModRepository
{
    public interface IConfig : BaseInstaller.IConfig
    {
    }

    private const string EnabledModsDirName = "Enabled";
    private const string DisabledModsSubdir = "Disabled";

    private readonly IConfig config;
    private readonly ITempDir tempDir;

    private readonly string enabledModsDir;
    private readonly string disabledModsDir;

    internal ModRepository(string modsDir, ITempDir tempDir, IConfig config)
    {
        var modsDirFullPath = Path.GetFullPath(modsDir);
        enabledModsDir = Path.Combine(modsDirFullPath, EnabledModsDirName);
        disabledModsDir = Path.Combine(modsDirFullPath, DisabledModsSubdir);
        this.tempDir = tempDir;
        this.config = config;
    }

    public ModPackage UploadPackage(string sourceFilePath)
    {
        var fileName = Path.GetFileName(sourceFilePath);

        var shouldBeEnabled = ListPackages()
            .Where(p => p.PackageName == fileName)
            .Select(p => p.Enabled)
            .FirstOrDefault(true);

        var destinationDirectoryPath = shouldBeEnabled ? enabledModsDir : disabledModsDir;
        var destinationFilePath = Path.Combine(destinationDirectoryPath, fileName);

        ExistingDirectoryOrCreate(destinationDirectoryPath);
        File.Copy(sourceFilePath, destinationFilePath, overwrite: true);

        return ModFilePackage(new FileInfo(destinationFilePath), shouldBeEnabled);
    }

    public string EnablePackage(string packagePath)
    {
        return MoveMod(packagePath, enabledModsDir);
    }

    public string DisablePackage(string packagePath)
    {
        return MoveMod(packagePath, disabledModsDir);
    }

    private static string MoveMod(string sourcePackagePath, string destinationParentPath)
    {
        ExistingDirectoryOrCreate(destinationParentPath);
        var destinationPackagePath = Path.Combine(destinationParentPath, Path.GetFileName(sourcePackagePath));
        if (Directory.Exists(sourcePackagePath))
        {
            Directory.Move(sourcePackagePath, destinationPackagePath);
        }
        else
        {
            File.Move(sourcePackagePath, destinationPackagePath);
        }
        return destinationPackagePath;
    }

    public IReadOnlyCollection<ModPackage> ListPackages() =>
        ListDir(path: enabledModsDir, enabled: true)
            .Concat(ListDir(path: disabledModsDir, enabled: false))
            .ToImmutableList();

    private IEnumerable<ModPackage> ListDir(string path, bool enabled)
    {
        var directoryInfo = new DirectoryInfo(path);
        if (!directoryInfo.Exists)
        {
            return Array.Empty<ModPackage>();
        }

        var options = new EnumerationOptions()
        {
            MatchType = MatchType.Win32,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            RecurseSubdirectories = false,
        };
        return directoryInfo.GetFiles("*", options).Select(fileInfo => ModFilePackage(fileInfo, enabled))
            .Concat(directoryInfo.GetDirectories("*", options).Select(fileInfo => ModDirectoryPackage(fileInfo, enabled)));
    }

    private ModPackage ModFilePackage(FileInfo modFileInfo, bool enabled) {
        var packageName = modFileInfo.Name;
        var fullPath = modFileInfo.FullName;
        var fsHash = FsHash(modFileInfo);
        return new ModPackage(
            PackageName: packageName,
            FullPath: fullPath,
            Enabled: enabled,
            FsHash: fsHash,
            Installer: new ModArchiveInstaller(packageName, fsHash, tempDir, config, fullPath)
        );
    }

    private ModPackage ModDirectoryPackage(DirectoryInfo modDirectoryInfo, bool enabled) {
        var packageName = $"{modDirectoryInfo.Name}{Path.DirectorySeparatorChar}";
        var fullPath = modDirectoryInfo.FullName;
        return new ModPackage(
            PackageName: packageName,
            FullPath: fullPath,
            Enabled: enabled,
            FsHash: null,
            Installer: new ModDirectoryInstaller(packageName, null, tempDir, config, fullPath)
        );
    }

    private bool IsEnabled(FileSystemInfo modFileSystemInfo) =>
        Directory.GetParent(modFileSystemInfo.FullName)?.FullName == enabledModsDir;

    /// <summary>
    /// Just a very simple has function to detect if the file might have changed.
    /// </summary>
    /// <param name="fileInfo"></param>
    /// <returns></returns>
    private static int FsHash(FileInfo fileInfo)
    {
        return unchecked((int)(fileInfo.LastWriteTimeUtc.Ticks ^ fileInfo.Length));
    }

    private static void ExistingDirectoryOrCreate(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }
}
