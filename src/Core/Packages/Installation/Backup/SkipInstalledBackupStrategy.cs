using System.IO.Abstractions;
using Core.Utils;

namespace Core.Packages.Installation.Backup;

/// <summary>
/// Prevents installed package contents from being backed up as original game files.
/// </summary>
internal class SkipInstalledBackupStrategy(
    IBackupStrategy inner, IFileSystem fs, IDictionary<RootedPath, DateTimeOffset> installedFiles,
    DateTimeOffset installTime) : IBackupStrategy
{
    internal class Provider<TEventHandler>(
        IBackupStrategyProvider<DateTimeOffset, TEventHandler> baseProvider,
        IEnumerable<(RootedPath Path, DateTimeOffset Time)> installedFiles,
        IFileSystem fs) : IBackupStrategyProvider<DateTimeOffset, TEventHandler>
        where TEventHandler : IBackupEventHandler
    {
        // All actions in a reconciliation must observe the same evolving installation state.
        private readonly Dictionary<RootedPath, DateTimeOffset> currentFiles = installedFiles
            .GroupBy(file => file.Path)
            .ToDictionary(group => group.Key, group => group.Max(file => file.Time));

        public IBackupStrategy BackupStrategy(DateTimeOffset backupTime, TEventHandler? eventHandler) =>
            new SkipInstalledBackupStrategy(baseProvider.BackupStrategy(backupTime, eventHandler), fs, currentFiles, backupTime);
    }

    public void PerformBackup(RootedPath path)
    {
        if (!installedFiles.TryGetValue(path, out var installedAt))
        {
            inner.PerformBackup(path);
        }
        else if (fs.File.Exists(path.Full) && fs.File.GetCreationTimeUtc(path.Full) > installedAt.UtcDateTime)
        {
            // An update replaced the previous owner's file. Preserve that new original
            // before another package overwrites it, using the previous owner's time.
            inner.DeleteBackup(path);
            inner.PerformBackup(path);
        }
    }

    public void AfterInstall(RootedPath path)
    {
        inner.AfterInstall(path);
        installedFiles[path] = installTime;
    }

    public void RestoreBackup(RootedPath path)
    {
        inner.RestoreBackup(path);
        installedFiles.Remove(path);
    }

    public void DeleteBackup(RootedPath path) => inner.DeleteBackup(path);
}
