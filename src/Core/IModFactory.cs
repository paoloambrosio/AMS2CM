using Core.Mods;

namespace Core;

public interface IModFactory
{
    IMod ManualInstallMod(string packageName, int packageFsHash, string tempDir);
    IMod GeneratedBootfiles(string generationBasePath);
}
