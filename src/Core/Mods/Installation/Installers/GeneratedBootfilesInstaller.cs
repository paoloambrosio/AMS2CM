using System.IO.Abstractions;
using Core.Packages.Installation.Installers;
using Core.Utils;
using PCarsTools;
using PCarsTools.Encryption;

namespace Core.Mods.Installation.Installers;

internal class GeneratedBootfilesInstaller : BaseDirectoryInstaller
{
    internal const string PakfilesDirectory = "Pakfiles";
    internal const string BootFlowPakFileName = "BOOTFLOW.bff";
    internal const string BootSplashPakFileName = "BOOTSPLASH.bff";
    internal const string PhysicsPersistentPakFileName = "PHYSICSPERSISTENT.bff";

    private readonly string pakPath;
    private readonly Lazy<int> versionHash;
    private readonly Lazy<IDirectoryInfo> extractionDirectory;
    private readonly Action<string, string> extractPak;
    private readonly string BmtFilesWildcard =
        Path.Combine("vehicles", "_data", "effects", "backfire", "*.bmt");

    public GeneratedBootfilesInstaller(string packageName, string gameInstallationDirectory, string tempDir) :
        this(new FileSystem(), packageName, gameInstallationDirectory, tempDir)
    {
    }

    internal GeneratedBootfilesInstaller(IFileSystem fs, string packageName, string gameInstallationDirectory, string tempDir,
        Action<string, string>? extractPak = null) : base(fs, packageName, null)
    {
        pakPath = Path.Combine(gameInstallationDirectory, PakfilesDirectory);
        var extractionPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        extractionDirectory = new(() => fs.Directory.CreateDirectory(extractionPath));
        versionHash = new(() => HashSources(fs));
        this.extractPak = extractPak ?? ExtractPak;
    }

    public override int? PackageVersionHash => versionHash.Value;

    protected override IDirectoryInfo Source => extractionDirectory.Value;

    private int HashSources(IFileSystem fs)
    {
        var useLiveFiles = fs.File.Exists(fs.Path.Combine(pakPath, PhysicsPersistentPakFileName));
        var files = new[] { BootFlowPakFileName, BootSplashPakFileName, PhysicsPersistentPakFileName }
            .Select(name => fs.FileInfo.New(fs.Path.Combine(pakPath,
                useLiveFiles || name == BootSplashPakFileName ? name : name + ".orig")))
            .ToArray();
        foreach (var file in files)
        {
            if (!file.Exists)
            {
                throw new FileNotFoundException("Missing game file required to generate bootfiles", file.FullName);
            }
        }
        return FileSystemHash.Hash32(files);
    }

    protected override void InstallAllFiles(InstallBody body)
    {
        GenerateBootfiles();
        base.InstallAllFiles(body);
    }

    protected override void InstallFile(RootedPath destinationPath, IFileInfo fileInfo)
    {
        FileSystem.File.Move(fileInfo.FullName, destinationPath.Full, overwrite: true);
    }

    #region Bootfiles Generation

    private void GenerateBootfiles()
    {
        ExtractPakFileFromGame(BootFlowPakFileName);
        ExtractPakFileFromGame(PhysicsPersistentPakFileName);
        CreateEmptyFile(ExtractedPakPath($"{PhysicsPersistentPakFileName}{BaseInstaller.RemoveFileSuffix}"));
        FileSystem.File.Copy(Path.Combine(pakPath, BootSplashPakFileName), ExtractedPakPath(BootFlowPakFileName));
        DeleteFromExtractedFiles(BmtFilesWildcard);
    }

    private void ExtractPakFileFromGame(string fileName)
    {
        var filePath = Path.Combine(pakPath, fileName);
        extractPak(filePath, Source.FullName);
    }

    // PCarsTools accesses disk directly; all other file operations use the injected filesystem.
    private static void ExtractPak(string filePath, string destinationDirectory)
    {
        BPakFileEncryption.SetKeyset(KeysetType.PC2AndAbove);
        using var pakFile = BPakFile.FromFile(filePath, withExtraInfo: true, outputWriter: TextWriter.Null);
        pakFile.UnpackAll(destinationDirectory);
    }

    private string ExtractedPakPath(string name) =>
        Path.Combine(Source.FullName, PakfilesDirectory, name);

    private void CreateEmptyFile(string path)
    {
        CreateParentDirectory(path);
        FileSystem.File.Create(path).Close();
    }

    private void CreateParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is not null)
            FileSystem.Directory.CreateDirectory(parent);
    }

    private void DeleteFromExtractedFiles(string wildcardRelative)
    {
        foreach (var file in FileSystem.Directory.EnumerateFiles(Source.FullName, wildcardRelative))
        {
            FileSystem.File.Delete(file);
        }
    }

    #endregion
}
