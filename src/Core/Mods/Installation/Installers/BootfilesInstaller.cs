using System.Collections.Immutable;
using System.IO.Abstractions;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Mods.Installation.Installers;

public class BootfilesInstaller : BaseModInstaller, IBootfilesInstaller
{
    public new interface IConfig : BaseModInstaller.IConfig
    {
        string BootfilesVehicleListDir { get; }
        string BootfilesTrackListDir { get; }
        string BootfilesDrivelineDir { get; }
    }

    public interface IEventHandler
    {
        void PostProcessingNotRequired();
        void PostProcessingStart();
        void ExtractingBootfiles(string? packageName);
        void PostProcessingVehicles();
        void PostProcessingTracks();
        void PostProcessingDrivelines();
        void PostProcessingEnd();
    }

    private readonly RootedPath gameInstallationPath;
    private readonly IBootfilesNaming bootfilesNaming;
    private readonly IEventHandler eventHandler;

    public BootfilesInstaller(IPackageInstaller? bootfilesPackageInstaller, string tempDir, IConfig config,
        string gameInstallationDir, IBootfilesNaming bootfilesNaming, IEventHandler eventHandler,
        PackageInstallationState? previousInstallation = null) :
        this(new FileSystem(), bootfilesPackageInstaller, tempDir, config,
            gameInstallationDir, bootfilesNaming, eventHandler, previousInstallation)
    {
    }

    public BootfilesInstaller(IFileSystem fileSystem, IPackageInstaller? bootfilesPackageInstaller, string tempDir,
        IConfig config, string gameInstallationDir, IBootfilesNaming bootfilesNaming, IEventHandler eventHandler,
        PackageInstallationState? previousInstallation = null) :
        base(fileSystem, PackageOrGenerated(fileSystem, bootfilesPackageInstaller, gameInstallationDir, tempDir, bootfilesNaming,
                previousInstallation),
            tempDir, config)
    {
        gameInstallationPath = new RootedPath(gameInstallationDir);
        VehicleListDir = gameInstallationPath.SubPath(config.BootfilesVehicleListDir);
        TrackListDir = gameInstallationPath.SubPath(config.BootfilesTrackListDir);
        DrivelineDir = gameInstallationPath.SubPath(config.BootfilesDrivelineDir);
        this.bootfilesNaming = bootfilesNaming;
        this.eventHandler = eventHandler;
    }

    private static IPackageInstaller PackageOrGenerated(IFileSystem fs, IPackageInstaller? bootfilesPackageInstaller,
        string gameInstallationDirectory, string tempDir, IBootfilesNaming bootfilesNaming,
        PackageInstallationState? previousInstallation) =>
        bootfilesPackageInstaller ?? new GeneratedBootfilesInstaller(fs, bootfilesNaming.GeneratedBootfilesName,
            gameInstallationDirectory, tempDir, previousInstallation: previousInstallation);

    // Bootfiles cannot have dependencies.
    public override IReadOnlySet<string> PackageDependencies => ImmutableHashSet<string>.Empty;

    protected override void Install(Action innerInstall, IBackupStrategy backupStrategy,
        ProcessingCallbacks<RootedPath> callbacks)
    {
        var packageNameIfNotGenerated = bootfilesNaming.IsGeneratedBootfiles(PackageName) ? null : PackageName;
        eventHandler.ExtractingBootfiles(packageNameIfNotGenerated);
        innerInstall();
    }

    public void NotRequired() => eventHandler.PostProcessingNotRequired();

    public void Configure(IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks)
    {
        var modConfigs = CollectModConfig();
        eventHandler.PostProcessingStart();
        ConfigureFile(VehicleListDir.SubPath(VehicleListFileName),
            original => ConfigureListContents(original, string.Join(Environment.NewLine, modConfigs.CrdFileEntries)),
            eventHandler.PostProcessingVehicles, backupStrategy, callbacks);
        ConfigureFile(TrackListDir.SubPath(TrackListFileName),
            original => ConfigureListContents(original, string.Join(Environment.NewLine, modConfigs.TrdFileEntries)),
            eventHandler.PostProcessingTracks, backupStrategy, callbacks);
        var drivelinePath = DrivelineDir.SubPath(DrivelineFileName);
        ConfigureFile(drivelinePath,
            original => ConfigureDrivelineContents(original,
                string.Join(Environment.NewLine + Environment.NewLine, DedupeRecordBlocks(modConfigs.DrivelineRecords)),
                drivelinePath.Full),
            eventHandler.PostProcessingDrivelines, backupStrategy, callbacks);
        eventHandler.PostProcessingEnd();
    }

    private void ConfigureFile(RootedPath path, Func<string, string> configureContents, Action report,
        IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks)
    {
        if (!callbacks.Accept(path))
        {
            callbacks.NotAccepted(path);
            return;
        }
        var exists = FileSystem.File.Exists(path.Full);
        var original = exists ? FileSystem.File.ReadAllText(path.Full) : "";
        var contents = configureContents(original);
        if (contents == original) return;

        report();
        backupStrategy.PerformBackup(path);
        callbacks.Before(path);
        try
        {
            FileSystem.Directory.GetParent(path.Full)?.Create();
            FileSystem.File.WriteAllText(path.Full, contents);
        }
        finally
        {
            backupStrategy.AfterInstall(path);
        }
        callbacks.After(path);
    }

    internal static string ConfigureListContents(string original, string entries)
    {
        var contents = RemoveConfigBlocks(original);
        return entries.Length == 0 ? contents : contents + ConfigBlock(entries);
    }

    internal static string ConfigureDrivelineContents(string original, string records, string path)
    {
        var contents = RemoveConfigBlocks(original);
        if (records.Length == 0) return contents;
        var block = ConfigBlock(records);
        if (contents.Length == 0) return block;

        var end = contents.LastIndexOf("END", StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException($"Could not find insertion point in {path}");
        return contents.Insert(end, block);
    }

    internal static string RemoveConfigBlocks(string contents)
    {
        const string beginMarker = "### BEGIN AMS2CM";
        const string endMarker = "### END AMS2CM";
        while (true)
        {
            var begin = contents.IndexOf(beginMarker, StringComparison.Ordinal);
            var end = contents.IndexOf(endMarker, StringComparison.Ordinal);
            if (begin < 0 && end < 0) return contents;
            var nextBegin = begin < 0 ? -1 : contents.IndexOf(beginMarker, begin + beginMarker.Length, StringComparison.Ordinal);
            if (begin < 0 || end < begin || (nextBegin >= 0 && nextBegin < end))
                throw new InvalidDataException("Malformed AMS2CM configuration block");

            // The surrounding newlines are part of WrapConfigBlock, not the original file.
            var from = begin;
            if (from > 0 && contents[from - 1] == '\n')
            {
                from--;
                if (from > 0 && contents[from - 1] == '\r') from--;
            }
            var to = end + endMarker.Length;
            if (to < contents.Length && contents[to] == '\r') to++;
            if (to < contents.Length && contents[to] == '\n') to++;
            contents = contents.Remove(from, to - from);
        }
    }

    protected override RootedPath VehicleListDir { get; }

    protected override RootedPath TrackListDir { get; }

    protected override RootedPath DrivelineDir { get; }

    protected override string WrapConfigBlock(string configBlock) => ConfigBlock(configBlock);

    private static string ConfigBlock(string configBlock) =>
        $"{Environment.NewLine}### BEGIN AMS2CM{Environment.NewLine}{configBlock}{Environment.NewLine}### END AMS2CM{Environment.NewLine}";

    private ConfigEntries CollectModConfig()
    {
        var modsGamePath = gameInstallationPath.SubPath(GameSupportedModRelativeDir);
        var directoryInfo = FileSystem.DirectoryInfo.New(modsGamePath.Full);
        if (!directoryInfo.Exists)
            return ConfigEntries.Empty;

        return directoryInfo.GetDirectories("*").Select(modDir =>
                modDir.EnumerateFiles($"{modDir.Name}.xml").Any()
                    ? ConfigEntries.Empty
                    : new ConfigEntries
                    (
                        FileLinesOrEmpty(modDir, VehicleListFileName),
                        FileLinesOrEmpty(modDir, TrackListFileName),
                        FileLinesOrEmpty(modDir, DrivelineFileName)
                    )
                ).Aggregate(ConfigEntries.Empty, ConfigEntries.Combine);
    }

    private string[] FileLinesOrEmpty(IDirectoryInfo parent, string fileName)
    {
        var filePath = Path.Combine(parent.FullName, fileName);
        return FileSystem.File.Exists(filePath) ? FileSystem.File.ReadAllLines(filePath) : Array.Empty<string>();
    }
}
