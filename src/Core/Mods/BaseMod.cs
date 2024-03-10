namespace Core.Mods;

public abstract class BaseMod : IMod
{
    protected readonly List<string> installedFiles = new();

    protected BaseMod(string packageName, int? packageFsHash)
    {
        PackageName = packageName;
        PackageFsHash = packageFsHash;
    }

    public string PackageName
    {
        get;
    }

    public int? PackageFsHash
    {
        get;
    }

    public IMod.InstalledState Installed
    {
        get;
        private set;
    }

    public IReadOnlyCollection<string> InstalledFiles => installedFiles;

    public ConfigEntries Install(string dstPath, Predicate<string> beforeEachFile)
    {
        if (Installed != IMod.InstalledState.NotInstalled)
        {
            throw new InvalidOperationException();
        }
        Installed = IMod.InstalledState.PartiallyInstalled;

        var config = InstallLoop(
                dstPath,
                beforeEachFile,
                CommonAfterEachFile(dstPath, DateTime.UtcNow)
            );

        Installed = IMod.InstalledState.Installed;

        return config;
    }

    protected abstract ConfigEntries InstallLoop(string dstPath, Predicate<string> beforeEachFile, Action<string> afterEachFile);

    private Action<string> CommonAfterEachFile(string dstPath, DateTime installDateTime) =>
        (string relativeFilePath) =>
        {
            installedFiles.Add(relativeFilePath);
            var fullPath = Path.Combine(dstPath, relativeFilePath);
            if (File.Exists(fullPath) && File.GetCreationTimeUtc(fullPath) > installDateTime)
            {
                File.SetCreationTimeUtc(fullPath, installDateTime);
            }
        };
}
