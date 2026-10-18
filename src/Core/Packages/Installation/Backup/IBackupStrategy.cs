using Core.Utils;

namespace Core.Packages.Installation.Backup;

public interface IBackupStrategy
{
    /// <summary>
    /// Performs backup of a file.
    /// If this fails, the destination must remain unchanged.
    /// </summary>
    /// <param name="path">File to back up.</param>
    public void PerformBackup(RootedPath path);

    /// <summary>
    /// Restores and deletes a previously performed backup.
    /// </summary>
    /// <param name="path">File to restore.</param>
    public void RestoreBackup(RootedPath path);

    /// <summary>
    /// Removes an existing backup without restoring it.
    /// </summary>
    /// <param name="path"></param>
    public void DeleteBackup(RootedPath path);

    /// <summary>
    /// Tracks managed files, including partially completed writes, for game update detection.
    /// </summary>
    /// <param name="path"></param>
    public void AfterInstall(RootedPath path);
}
