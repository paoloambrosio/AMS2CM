﻿
namespace Core.Mods;

public interface IModRepository
{
    ModPackage UploadMod(string sourceFilePath);
    string EnableMod(string packagePath);
    string DisableMod(string packagePath);
    IReadOnlyCollection<ModPackage> ListEnabledMods();
    IReadOnlyCollection<ModPackage> ListDisabledMods();
}

public record ModPackage
(
    string PackageName,
    string FullPath,
    bool Enabled,
    int? FsHash
);
