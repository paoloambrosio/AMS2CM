using System.IO.Abstractions;
using Core.IO;
using Core.Packages.Repository;

namespace Core.API;

internal interface IModPackageOperations
{
    void AddNewMod(string packagePath);
    void DeleteMod(string packagePath);
    string EnableMod(string packagePath);
    string DisableMod(string packagePath);
}

internal sealed class ModPackageOperations(
    IPackageRepository packageRepository,
    ISafeFileDelete safeFileDelete,
    IFileSystem fileSystem) : IModPackageOperations
{
    public void AddNewMod(string packagePath)
    {
        if (fileSystem.File.GetAttributes(packagePath).HasFlag(FileAttributes.Directory))
        {
            throw new Exception($"{packagePath} is a directory");
        }
        packageRepository.Upload(packagePath);
    }

    public void DeleteMod(string packagePath) => safeFileDelete.SafeDelete(packagePath);
    public string EnableMod(string packagePath) => packageRepository.Enable(packagePath);
    public string DisableMod(string packagePath) => packageRepository.Disable(packagePath);
}
