
namespace Core.Mods;

public interface IModRepository
{
    ModPackage UploadPackage(string sourceFilePath);
    string EnablePackage(string packagePath);
    string DisablePackage(string packagePath);
    IReadOnlyCollection<ModPackage> ListPackages();
}

public record ModPackage
(
    string PackageName,
    string FullPath,
    bool Enabled,
    int? FsHash,
    IInstaller Installer
);
