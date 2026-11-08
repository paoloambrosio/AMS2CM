using System.Collections.Immutable;
using System.IO.Abstractions;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Core.Mods.Installation.Installers;

/**
 * Wrapper over mod installer that generates configuration for game or bootfiles.
 */
public class ModInstaller : BaseModInstaller
{
    public new interface IConfig : BaseModInstaller.IConfig
    {
        IEnumerable<string> ExcludedFromConfig { get; }
        bool GenerateModDetails { get; }
    }

    private readonly Matcher filesToConfigureMatcher;
    private readonly bool generateModDetails;

    private IReadOnlyCollection<string> bootfilesDependency = Array.Empty<string>();
    private readonly string bootfilesPackageName;

    private readonly RootedPath modConfigPath;
    private readonly string modName;
    private readonly Dictionary<RootedPath, string?> originalConfiguration = new();
    private bool hasDrivelineRecords;

    internal ModInstaller(IPackageInstaller inner, IDirectoryInfo tempDir, IConfig config,
        IDirectoryInfo gameInstallationDir, string bootfilesPackageName) :
        base(inner, tempDir, config)
    {
        this.bootfilesPackageName = bootfilesPackageName;
        filesToConfigureMatcher = Matchers.ExcludingPatterns(config.ExcludedFromConfig);
        generateModDetails = config.GenerateModDetails;

        var normalisedName = string.Concat(
            Path.GetFileNameWithoutExtension(inner.PackageName)
                .Where(char.IsAsciiLetterOrDigit));
        var hexVersionHash = (inner.PackageVersionHash ?? 0).ToString("x");
        modName = $"{normalisedName}_{hexVersionHash}";

        modConfigPath =
            new PathRoot(gameInstallationDir).Combine(GameSupportedModRelativeDir, modName);
    }

    public override IReadOnlySet<string> PackageDependencies =>
        Inner.PackageDependencies.Concat(bootfilesDependency).ToImmutableHashSet();

    protected override RootedPath VehicleListDir => modConfigPath;

    protected override RootedPath TrackListDir => modConfigPath;

    protected override RootedPath DrivelineDir => modConfigPath;

    protected override void Configure(IBackupStrategy backupStrategy,
        ProcessingCallbacks<RootedPath> callbacks)
    {
        if (ConfiguredByPackage()) return;
        var records = FindDrivelineRecords();
        hasDrivelineRecords = records.Count > 0;
        InsertDrivelineRecords(records, backupStrategy, callbacks);
        Reconfigure(backupStrategy, callbacks);
    }

    /// <summary>
    /// Refreshes lists on the instance that installed this mod, preserving its driveline configuration.
    /// </summary>
    public override void Reconfigure(IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks)
    {
        if (ConfiguredByPackage()) return;
        var vehicles = FindCrdFileEntries();
        var tracks = FindTrdFileEntries();
        UpdateList(VehicleListDir.Combine(VehicleListFileName), vehicles, backupStrategy, callbacks);
        UpdateList(TrackListDir.Combine(TrackListFileName), tracks, backupStrategy, callbacks);

        var hasConfiguration = vehicles.Count > 0 || tracks.Count > 0 || hasDrivelineRecords;
        var needsManifest = hasConfiguration && generateModDetails && tracks.Count == 0;
        UpdateConfigurationFile(modConfigPath.Combine($"{modName}.xml"),
            original => needsManifest ? ModManifest : original, backupStrategy, callbacks);
        bootfilesDependency = hasConfiguration && !needsManifest ? [bootfilesPackageName] : [];
    }

    private bool ConfiguredByPackage() => FileEntriesToConfigure()
        .Any(p => p.StartsWith(GameSupportedModRelativeDir, StringComparison.OrdinalIgnoreCase));

    private void UpdateList(RootedPath path, List<string> entries,
        IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks) =>
        UpdateConfigurationFile(path,
            original => entries.Count == 0 ? original : original + string.Join(Environment.NewLine, entries),
            backupStrategy, callbacks);

    private void UpdateConfigurationFile(RootedPath path, Func<string?, string?> configure,
        IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks)
    {
        var current = FileSystem.File.Exists(path.Full) ? FileSystem.File.ReadAllText(path.Full) : null;
        if (!originalConfiguration.TryGetValue(path, out var original))
        {
            original = current;
            originalConfiguration.Add(path, original);
        }
        var contents = configure(original);
        if (contents == current) return;

        var owned = InstalledFiles.Contains(path);
        var effectiveCallbacks = owned ? new ProcessingCallbacks<RootedPath>() : callbacks;
        effectiveCallbacks.Wrap(() =>
        {
            if (contents is null)
            {
                backupStrategy.RestoreBackup(path);
                ForgetInstalledFile(path);
                return;
            }
            if (!owned) backupStrategy.PerformBackup(path);
            RecordInstalledFile(path);
            try
            {
                FileSystem.Directory.GetParent(path.Full)?.Create();
                FileSystem.File.WriteAllText(path.Full, contents);
            }
            finally
            {
                backupStrategy.AfterInstall(path);
            }
        }, path);
    }

    private List<string> FindCrdFileEntries() =>
        FileEntriesToConfigure()
            .Where(p => p.EndsWith(".crd", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private List<string> FindTrdFileEntries() =>
        FileEntriesToConfigure()
            .Where(p => p.EndsWith(".trd", StringComparison.OrdinalIgnoreCase))
            .Select(fp => $"{Path.GetDirectoryName(fp)}{Path.DirectorySeparatorChar}@{Path.GetFileName(fp)}")
            .ToList();

    private IEnumerable<string> FileEntriesToConfigure() =>
        Inner.InstalledFiles
            .Where(rp => rp.Root != StagingRoot)
            .Where(rp => FileSystem.File.Exists(rp.Full))
            .Select(rp => rp.Relative)
            .Where(p => filesToConfigureMatcher.Match(p).HasMatches);

    private List<string> FindDrivelineRecords()
    {
        var recordBlocks = new List<string>();
        if (!FileSystem.Directory.Exists(StagingRoot.Full))
        {
            return recordBlocks;
        }

        foreach (var configFile in FileSystem.Directory.EnumerateFiles(StagingRoot.Full))
        {
            var recordIndent = -1;
            var recordLines = new List<string>();
            foreach (var line in FileSystem.File.ReadAllLines(configFile))
            {
                // Read each line until we find one with RECORD
                if (recordIndent < 0)
                {
                    recordIndent = line.IndexOf("RECORD", StringComparison.Ordinal);
                }

                if (recordIndent < 0)
                {
                    continue;
                }

                // Once it finds a blank line, create a record block and start over
                if (string.IsNullOrWhiteSpace(line))
                {
                    recordBlocks.Add(string.Join(Environment.NewLine, recordLines));
                    recordIndent = -1;
                    recordLines.Clear();
                    continue;
                }

                // Otherwise add the line to the current record lines
                var lineNoIndent = line.Substring(recordIndent).TrimEnd();
                recordLines.Add(lineNoIndent);
            }

            // Create a record block also if the file finshed on a record line
            if (recordIndent >= 0)
            {
                recordBlocks.Add(string.Join(Environment.NewLine, recordLines));
            }
        }

        return recordBlocks;
    }

    public string ModManifest =>
        @$"<?xml version=""1.0""?>
<Reflection>
    <class name=""BRTTIRefCount"" base=""root class"" />
    <class name=""BPersistent"" base=""BRTTIRefCount"">
        <prop name=""Name"" type=""String"" />
    </class>
    <class name=""ModDetails"" base=""BPersistent"">
        <prop name=""DisplayName"" type=""String"" />
    </class>
    <data class=""ModDetails"" id=""0x{PackageVersionHash:x08}"">
        <prop name=""Name"" data=""{modName}"" />
        <prop name=""DisplayName"" data=""{PackageName}"" />
    </data>
</Reflection>";
}
