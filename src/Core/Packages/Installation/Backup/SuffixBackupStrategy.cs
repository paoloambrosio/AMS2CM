using System.IO.Abstractions;

namespace Core.Packages.Installation.Backup;

public class SuffixBackupStrategy : MoveFileBackupStrategy
{
    internal class Provider<TState, TEventHandler>(IFileSystem fileSystem) : IBackupStrategyProvider<TState, TEventHandler>
        where TEventHandler : IBackupEventHandler
    {
        public IBackupStrategy BackupStrategy(TState? _, TEventHandler? eventHandler) =>
            new SuffixBackupStrategy(fileSystem, eventHandler);
    }

    private class BackupFileNaming : IBackupFileNaming
    {
        private const string BackupSuffix = ".orig";

        public string ToBackup(string fullPath) => $"{fullPath}{BackupSuffix}";
        public bool IsBackup(string fullPath) => fullPath.EndsWith(BackupSuffix);
    }

    public SuffixBackupStrategy(IFileSystem fileSystem, IBackupEventHandler? eventHandler) :
        base(fileSystem, new BackupFileNaming(), eventHandler)
    {
    }
}
