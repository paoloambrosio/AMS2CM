using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Mods.Installation.Installers;

public class BootfilesInstaller : BaseModInstaller, IReconfigurablePackageInstaller
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
        string gameInstallationDir, IBootfilesNaming bootfilesNaming, IEventHandler eventHandler) :
        this(new FileSystem(), bootfilesPackageInstaller, tempDir, config,
            gameInstallationDir, bootfilesNaming, eventHandler)
    {
    }

    public BootfilesInstaller(IFileSystem fileSystem, IPackageInstaller? bootfilesPackageInstaller, string tempDir,
        IConfig config, string gameInstallationDir, IBootfilesNaming bootfilesNaming, IEventHandler eventHandler) :
        base(fileSystem, PackageOrGenerated(bootfilesPackageInstaller, gameInstallationDir, tempDir, bootfilesNaming),
            tempDir, config)
    {
        gameInstallationPath = new RootedPath(gameInstallationDir);
        VehicleListDir = gameInstallationPath.SubPath(config.BootfilesVehicleListDir);
        TrackListDir = gameInstallationPath.SubPath(config.BootfilesTrackListDir);
        DrivelineDir = gameInstallationPath.SubPath(config.BootfilesDrivelineDir);
        this.bootfilesNaming = bootfilesNaming;
        this.eventHandler = eventHandler;
    }

    private static IPackageInstaller PackageOrGenerated(IPackageInstaller? bootfilesPackageInstaller,
        string gameInstallationDirectory, string tempDir, IBootfilesNaming bootfilesNaming) =>
        bootfilesPackageInstaller ?? new GeneratedBootfilesInstaller(bootfilesNaming.GeneratedBootfilesName,
            gameInstallationDirectory, tempDir);

    private int? generatedVersionHash;

    public override int? PackageVersionHash =>
        Inner is GeneratedBootfilesInstaller ? generatedVersionHash : Inner.PackageVersionHash;

    internal void Prepare(PackageInstallationState? previousBootfiles)
    {
        if (Inner is GeneratedBootfilesInstaller)
        {
            generatedVersionHash = GeneratedBootfilesInstaller.VersionHash(
                FileSystem, gameInstallationPath.Full, previousBootfiles);
        }
    }

    public bool IsRequired => CollectModConfig().Any();

    // Bootfiles cannot have dependencies.
    public override IReadOnlySet<string> PackageDependencies => ImmutableHashSet<string>.Empty;

    protected override void Install(Action innerInstall, IBackupStrategy backupStrategy,
        ProcessingCallbacks<RootedPath> callbacks)
    {
        var modConfigs = CollectModConfig();
        if (modConfigs.None())
        {
            eventHandler.PostProcessingNotRequired();
            return;
        }

        eventHandler.PostProcessingStart();
        var packageNameIfNotGenerated = bootfilesNaming.IsGeneratedBootfiles(PackageName) ? PackageName : null;
        eventHandler.ExtractingBootfiles(packageNameIfNotGenerated);

        // Uninstalls have restored the original game archives by this point.
        Prepare(null);
        innerInstall();

        if (modConfigs.CrdFileEntries.Count > 0)
        {
            eventHandler.PostProcessingVehicles();
            AppendCrdFileEntries(modConfigs.CrdFileEntries, backupStrategy, callbacks);
        }
        if (modConfigs.TrdFileEntries.Count > 0)
        {
            eventHandler.PostProcessingTracks();
            AppendTrdFileEntries(modConfigs.TrdFileEntries, backupStrategy, callbacks);
        }
        if (modConfigs.DrivelineRecords.Count > 0)
        {
            eventHandler.PostProcessingDrivelines();
            InsertDrivelineRecords(modConfigs.DrivelineRecords, backupStrategy, callbacks);
        }
        eventHandler.PostProcessingEnd();
    }

    public void Configure(IReadOnlySet<RootedPath> installedFiles, IBackupStrategy backupStrategy,
        ProcessingCallbacks<RootedPath> callbacks)
    {
        var config = CollectModConfig();
        var updates = new List<(RootedPath Path, string Contents, Action Notify)>();
        AddUpdate(VehicleListDir.SubPath(VehicleListFileName),
            string.Join(Environment.NewLine, config.CrdFileEntries), false, eventHandler.PostProcessingVehicles);
        AddUpdate(TrackListDir.SubPath(TrackListFileName),
            string.Join(Environment.NewLine, config.TrdFileEntries), false, eventHandler.PostProcessingTracks);
        AddUpdate(DrivelineDir.SubPath(DrivelineFileName),
            string.Join($"{Environment.NewLine}{Environment.NewLine}", DedupeRecordBlocks(config.DrivelineRecords)),
            true, eventHandler.PostProcessingDrivelines);

        if (updates.Count == 0)
        {
            eventHandler.PostProcessingNotRequired();
            return;
        }

        eventHandler.PostProcessingStart();
        foreach (var (path, contents, notify) in updates)
        {
            var owned = installedFiles.Contains(path);
            (owned ? new ProcessingCallbacks<RootedPath>() : callbacks).Wrap(() =>
            {
                notify();
                if (!owned)
                {
                    backupStrategy.PerformBackup(path);
                }

                FileSystem.Directory.CreateDirectory(Path.GetDirectoryName(path.Full)!);
                FileSystem.File.WriteAllText(path.Full, contents);
                backupStrategy.AfterInstall(path);
            }, path);
        }

        eventHandler.PostProcessingEnd();
        return;

        void AddUpdate(RootedPath path, string entries, bool driveline, Action notify)
        {
            if (!installedFiles.Contains(path) && !callbacks.Accept(path))
            {
                return;
            }

            var exists = FileSystem.File.Exists(path.Full);
            var oldContents = exists ? FileSystem.File.ReadAllText(path.Full) : "";
            var contents = ManagedConfigBlock.Replace(oldContents, "");
            if (entries.Length > 0)
            {
                var block = WrapConfigBlock(entries);
                if (driveline && contents.Length > 0)
                {
                    var endIndex = contents.LastIndexOf("END", StringComparison.Ordinal);
                    if (endIndex < 0)
                    {
                        throw new Exception("Could not find insertion point in driveline file");
                    }

                    contents = contents.Insert(endIndex, block);
                }
                else
                {
                    contents += block;
                }
            }

            if (contents != oldContents)
            {
                updates.Add((path, contents, notify));
            }
        }
    }

    private static readonly Regex ManagedConfigBlock = new(
        @"(?:\r\n|\n)?### BEGIN AMS2CM\r?\n.*?\r?\n### END AMS2CM(?:\r\n|\n)?",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    protected override RootedPath VehicleListDir { get; }

    protected override RootedPath TrackListDir { get; }

    protected override RootedPath DrivelineDir { get; }

    protected override string WrapConfigBlock(string configBlock) =>
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
