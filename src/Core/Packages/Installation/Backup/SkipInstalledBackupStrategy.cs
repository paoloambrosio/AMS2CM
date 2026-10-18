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
    internal IDictionary<RootedPath, InstalledFileState> FileStates { get; init; } =
        new Dictionary<RootedPath, InstalledFileState>();

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

        internal Dictionary<RootedPath, InstalledFileState> FileStates { get; init; } = [];

        public IBackupStrategy BackupStrategy(DateTimeOffset backupTime, TEventHandler? eventHandler)
        {
            var inner = baseProvider.BackupStrategy(backupTime, eventHandler);
            var strategy = new SkipInstalledBackupStrategy(inner, fs, currentFiles, backupTime) { FileStates = FileStates };
            if (inner is SkipUpdatedBackupStrategy updated)
            {
                updated.IsUpdated = path => currentFiles.TryGetValue(path, out var installedAt) &&
                    strategy.FileUpdated(path, installedAt);
            }
            return strategy;
        }
    }

    public void PerformBackup(RootedPath path)
    {
        if (!installedFiles.TryGetValue(path, out var installedAt))
        {
            inner.PerformBackup(path);
        }
        else if (FileUpdated(path, installedAt))
        {
            // An update replaced the previous owner's file. Preserve that new original
            // before another package overwrites it, using the previous owner's metadata.
            inner.DeleteBackup(path);
            inner.PerformBackup(path);
        }
    }

    public void AfterInstall(RootedPath path)
    {
        // Invalidate the former owner's baseline before reading the new file's attributes.
        installedFiles[path] = installTime;
        FileStates[path] = InstalledFileState.Unknown;
        try
        {
            inner.AfterInstall(path);
        }
        finally
        {
            // A write already ran, even when subsequent bookkeeping failed.
            FileStates[path] = InstalledFileState.CaptureAfterWrite(fs, path);
        }
    }

    public void RestoreBackup(RootedPath path)
    {
        // Historical states can contain several spellings or owners of one file.
        if (!installedFiles.ContainsKey(path)) return;
        inner.RestoreBackup(path);
        installedFiles.Remove(path);
        FileStates.Remove(path);
    }

    private bool FileUpdated(RootedPath path, DateTimeOffset installedAt)
    {
        if (FileStates.TryGetValue(path, out var previous))
        {
            // An unknown baseline must never fall through to timestamp inference.
            var matches = previous.Matches(fs, path);
            return !matches && fs.File.Exists(path.Full);
        }
        return fs.File.Exists(path.Full) && fs.File.GetCreationTimeUtc(path.Full) > installedAt.UtcDateTime;
    }

    public void DeleteBackup(RootedPath path) => inner.DeleteBackup(path);
}
