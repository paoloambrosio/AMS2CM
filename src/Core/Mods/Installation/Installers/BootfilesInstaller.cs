using System.Collections.Immutable;
using System.IO.Abstractions;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Mods.Installation.Installers;

public class BootfilesInstaller : BaseModInstaller
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

    private readonly PathRoot gameRoot;
    private readonly IBootfilesNaming bootfilesNaming;
    private readonly IEventHandler eventHandler;

    public BootfilesInstaller(IPackageInstaller? bootfilesPackageInstaller, IDirectoryInfo tempDir,
        IConfig config, IDirectoryInfo gameInstallationDir, IBootfilesNaming bootfilesNaming,
        IEventHandler eventHandler) :
        base(PackageOrGenerated(bootfilesPackageInstaller, gameInstallationDir, tempDir, bootfilesNaming),
            tempDir, config)
    {
        gameRoot = new PathRoot(gameInstallationDir);
        VehicleListDir = gameRoot.Combine(config.BootfilesVehicleListDir);
        TrackListDir = gameRoot.Combine(config.BootfilesTrackListDir);
        DrivelineDir = gameRoot.Combine(config.BootfilesDrivelineDir);
        this.bootfilesNaming = bootfilesNaming;
        this.eventHandler = eventHandler;
    }

    private static IPackageInstaller PackageOrGenerated(IPackageInstaller? bootfilesPackageInstaller,
        IDirectoryInfo gameInstallationDirectory, IDirectoryInfo tempDir, IBootfilesNaming bootfilesNaming) =>
        bootfilesPackageInstaller ?? new GeneratedBootfilesInstaller(bootfilesNaming.GeneratedBootfilesName,
            gameInstallationDirectory, tempDir);

    // Bootfiles cannot have dependencies.
    public override IReadOnlySet<string> PackageDependencies => ImmutableHashSet<string>.Empty;

    protected override void Install(Action innerInstall, IBackupStrategy backupStrategy,
        ProcessingCallbacks<RootedPath> callbacks)
    {
        if (CollectModConfig().None())
        {
            NotRequired();
            return;
        }
        var packageNameIfNotGenerated = bootfilesNaming.IsGeneratedBootfiles(PackageName) ? PackageName : null;
        eventHandler.ExtractingBootfiles(packageNameIfNotGenerated);
        innerInstall();
    }

    public void NotRequired() => eventHandler.PostProcessingNotRequired();

    public void SelectSources(IReadOnlyDictionary<string, PackageInstallationState> state)
    {
        if (Inner is GeneratedBootfilesInstaller generated)
        {
            generated.SelectSources(state);
        }
    }

    public void Prepare(IReadOnlyDictionary<string, PackageInstallationState> state)
    {
        SelectSources(state);
        if (Inner is GeneratedBootfilesInstaller generated)
        {
            generated.Prepare();
        }
    }

    protected override void Configure(IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks) =>
        Reconfigure(backupStrategy, callbacks);

    public override void Reconfigure(IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks)
    {
        eventHandler.PostProcessingStart();
        Configure(backupStrategy, callbacks, CollectModConfig());
        eventHandler.PostProcessingEnd();
    }

    private void Configure(IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks, ConfigEntries modConfigs)
    {
        ConfigureFile(VehicleListDir.Combine(VehicleListFileName),
            original => ConfigureListContents(original, string.Join(Environment.NewLine, modConfigs.CrdFileEntries)),
            eventHandler.PostProcessingVehicles, backupStrategy, callbacks);
        ConfigureFile(TrackListDir.Combine(TrackListFileName),
            original => ConfigureListContents(original, string.Join(Environment.NewLine, modConfigs.TrdFileEntries)),
            eventHandler.PostProcessingTracks, backupStrategy, callbacks);
        ConfigureFile(DrivelineDir.Combine(DrivelineFileName),
            original => ConfigureDrivelineContents(original,
                string.Join(Environment.NewLine + Environment.NewLine, DedupeRecordBlocks(modConfigs.DrivelineRecords))),
            eventHandler.PostProcessingDrivelines, backupStrategy, callbacks);
    }

    private void ConfigureFile(RootedPath path, Func<string, string> configureContents, Action report,
        IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks)
    {
        callbacks = InstalledFiles.Contains(path) ? new ProcessingCallbacks<RootedPath>() : callbacks;
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
            RecordInstalledFile(path);
            FileSystem.File.WriteAllText(path.Full, contents);
        }
        finally
        {
            backupStrategy.AfterInstall(path);
        }
        callbacks.After(path);
    }

    internal string ConfigureListContents(string original, string entries)
    {
        var contents = RemoveConfigBlocks(original);
        return entries.Length == 0 ? contents : contents + WrapConfigBlock(entries);
    }

    internal string ConfigureDrivelineContents(string original, string records)
    {
        var contents = RemoveConfigBlocks(original);
        if (records.Length == 0) return contents;
        var block = WrapConfigBlock(records);
        if (contents.Length == 0) return block;

        var end = contents.LastIndexOf("END", StringComparison.Ordinal);
        return end < 0
            ? throw new InvalidDataException("Could not find insertion point in driveline file")
            : contents.Insert(end, block);
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

    protected override string WrapConfigBlock(string configBlock) =>
        $"{Environment.NewLine}### BEGIN AMS2CM{Environment.NewLine}{configBlock}{Environment.NewLine}### END AMS2CM{Environment.NewLine}";

    private ConfigEntries CollectModConfig()
    {
        var modsGamePath = gameRoot.Combine(GameSupportedModRelativeDir);
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
