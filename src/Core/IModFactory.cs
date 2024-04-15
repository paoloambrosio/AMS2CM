using Core.Mods;

namespace Core;

public interface IModFactory
{
    IInstallable ManualInstallMod(string packageName, int packageFsHash, string tempDir);
    IInstallable GeneratedBootfiles(string generationBasePath);
}
