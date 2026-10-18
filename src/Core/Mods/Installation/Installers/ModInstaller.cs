using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
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

    internal ModInstaller(IPackageInstaller inner, string tempDir, IConfig config,
        string gameInstallationDir, string bootfilesPackageName) :
        this(new FileSystem(), inner, tempDir, config, gameInstallationDir, bootfilesPackageName)
    {
    }

    internal ModInstaller(IFileSystem fileSystem, IPackageInstaller inner, string tempDir, IConfig config,
        string gameInstallationDir, string bootfilesPackageName) :
        base(fileSystem, inner, tempDir, config)
    {
        this.bootfilesPackageName = bootfilesPackageName;
        filesToConfigureMatcher = Matchers.ExcludingPatterns(config.ExcludedFromConfig);
        generateModDetails = config.GenerateModDetails;

        modName = ConfigurationName(inner.PackageName, inner.PackageVersionHash);

        modConfigPath = new RootedPath(gameInstallationDir, Path.Combine(GameSupportedModRelativeDir, modName));
    }

    public override int InstallationFormatVersion => 1;

    internal static string ConfigurationName(string packageName, int? versionHash)
    {
        var identity = packageName.Replace('/', '\\').ToUpperInvariant();
        var stem = string.Concat(Path.GetFileNameWithoutExtension(identity.TrimEnd('\\'))
            .Where(char.IsAsciiLetterOrDigit).Take(32));
        if (stem.Length == 0) stem = "MOD";
        var identityHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"{stem}_{identityHash}_{versionHash ?? 0:x}";
    }

    public override IReadOnlySet<string> PackageDependencies =>
        Inner.PackageDependencies.Concat(bootfilesDependency).ToImmutableHashSet();

    protected override RootedPath VehicleListDir => modConfigPath;

    protected override RootedPath TrackListDir => modConfigPath;

    protected override RootedPath DrivelineDir => modConfigPath;

    protected override void Install(Action innerInstall, IBackupStrategy backupStrategy,
        ProcessingCallbacks<RootedPath> callbacks)
    {
        innerInstall();

        var gameSupportedMod = FileEntriesToConfigure()
            .Any(p => p.StartsWith(GameSupportedModRelativeDir));
        var modConfig = gameSupportedMod
            ? ConfigEntries.Empty
            : new ConfigEntries(FindCrdFileEntries(), FindTrdFileEntries(), FindDrivelineRecords());

        if (modConfig.None())
        {
            return;
        }

        AppendCrdFileEntries(modConfig.CrdFileEntries, backupStrategy, callbacks);
        AppendTrdFileEntries(modConfig.TrdFileEntries, backupStrategy, callbacks);
        InsertDrivelineRecords(modConfig.DrivelineRecords, backupStrategy, callbacks);
        if (generateModDetails && !modConfig.TrdFileEntries.Any())
        {
            SafeWriteAllText(modConfigPath.SubPath($"{modName}.xml"), ModManifest, backupStrategy, callbacks);
        }
        else
        {
            bootfilesDependency = new[] { bootfilesPackageName };
        }
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
        InstalledFiles
            .Where(rp => FileSystem.File.Exists(rp.Full))
            .Select(rp => rp.Relative)
            .Where(p => filesToConfigureMatcher.Match(p).HasMatches);

    private List<string> FindDrivelineRecords()
    {
        var recordBlocks = new List<string>();
        if (!FileSystem.Directory.Exists(StagingFullPath))
        {
            return recordBlocks;
        }

        foreach (var configFile in FileSystem.Directory.EnumerateFiles(StagingFullPath)
                     .Order(StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal))
            recordBlocks.AddRange(DrivelineRecords.Parse(FileSystem.File.ReadAllText(configFile)));

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
