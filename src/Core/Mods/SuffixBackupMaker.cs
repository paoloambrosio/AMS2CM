namespace Core.Mods;

public class SuffixBackupMaker : IBackupMaker
{
    public const string DefaultSuffix = ".orig";

    private readonly string suffix;

    public SuffixBackupMaker() : this(DefaultSuffix)
    {
    }

    public SuffixBackupMaker(string suffix)
    {
        this.suffix = suffix;
    }

    private string BackupFileName(string originalFileName) => $"{originalFileName}{suffix}";

    public void PerformBackup(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            BackupFile(fullPath);
        }
    }

    private void BackupFile(string fullPath)
    {
        var backupFile = BackupFileName(fullPath);
        if (File.Exists(backupFile))
        {
            // TODO message: overwriting already installed file
            // TODO WHAT ON EARTH IS THIS?!
            File.Delete(fullPath);
        }
        else
        {
            File.Move(fullPath, backupFile);
        }
    }

    public void RestoreBackup(string fullPath)
    {
        var backupFilePath = BackupFileName(fullPath);
        if (File.Exists(backupFilePath))
        {
            File.Move(backupFilePath, fullPath);
        }
    }

    public void DeleteBackup(string fullPath)
    {
        var backupFilePath = BackupFileName(fullPath);
        if (File.Exists(backupFilePath))
        {
            File.Delete(backupFilePath);
        }
    }
}
