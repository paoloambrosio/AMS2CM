namespace Core.Mods;

public abstract class ExtractedMod : BaseMod
{
    protected readonly string extractedPath;

    internal ExtractedMod(string packageName, int? packageFsHash, string extractedPath)
    : base(packageName, packageFsHash)
    {
        this.extractedPath = extractedPath;
    }

    protected override ConfigEntries InstallLoop(string dstPath, Predicate<string> beforeEachFile, Action<string> afterEachFile)
    {
        foreach (var rootPath in ExtractedRootDirs())
        {
            JsgmeFileInstaller.InstallFiles(rootPath, dstPath,
                relativePath =>
                    FileShouldBeInstalled(relativePath) &&
                    beforeEachFile(relativePath),
                afterEachFile);
        }
        return GenerateConfig();
    }

    protected abstract IEnumerable<string> ExtractedRootDirs();

    protected abstract ConfigEntries GenerateConfig();

    protected virtual bool FileShouldBeInstalled(string relativePath) => true;
}