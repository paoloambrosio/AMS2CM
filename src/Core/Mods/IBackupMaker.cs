namespace Core.Mods;

public interface IBackupMaker
{
    public void PerformBackup(string fullPath);
    public void RestoreBackup(string fullPath);
    public void DeleteBackup(string fullPath);
}
