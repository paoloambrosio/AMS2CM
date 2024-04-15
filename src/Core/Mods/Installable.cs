namespace Core.Mods;

public abstract class Installable : IInstallable
{
    protected readonly List<string> installedFiles = new();

    protected Installable(string packageName, int? packageFsHash)
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

    public IInstallable.InstalledState Installed
    {
        get;
        private set;
    }

    public IReadOnlyCollection<string> InstalledFiles => installedFiles;

    public ConfigEntries Install(string dstPath, Predicate<string> beforeEachFile)
    {
        if (Installed != IInstallable.InstalledState.NotInstalled)
        {
            throw new InvalidOperationException();
        }
        using var installer = NewInstaller();
        Installed = IInstallable.InstalledState.PartiallyInstalled;

        var config = installer.Install(
                dstPath,
                beforeEachFile,
                CommonAfterEachFile(dstPath, DateTime.UtcNow)
            );

        Installed = IInstallable.InstalledState.Installed;

        return config;
    }

    protected abstract IInstaller NewInstaller();

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
