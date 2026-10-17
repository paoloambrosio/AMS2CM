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
    private readonly Lazy<DirectoryInfo> extractionDirectory;
    private readonly string BmtFilesWildcard =
        Path.Combine("vehicles", "_data", "effects", "backfire", "*.bmt");

    public GeneratedBootfilesInstaller(string packageName, string gameInstallationDirectory, string tempDir) :
        this(new FileSystem(), packageName, gameInstallationDirectory, tempDir)
    {
    }

    internal GeneratedBootfilesInstaller(IFileSystem fs, string packageName, string gameInstallationDirectory, string tempDir) :
        base(packageName, null)
    {
        pakPath = Path.Combine(gameInstallationDirectory, PakfilesDirectory);
        var extractionPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        extractionDirectory = new(() => Directory.CreateDirectory(extractionPath));
        versionHash = new(() => HashSources(fs));
    }

    public override int? PackageVersionHash => versionHash.Value;

    protected override DirectoryInfo Source => extractionDirectory.Value;

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

    protected override void InstallFile(RootedPath destinationPath, FileInfo fileInfo)
    {
        File.Move(fileInfo.FullName, destinationPath.Full, overwrite: true);
    }

    #region Bootfiles Generation

    private void GenerateBootfiles()
    {
        ExtractPakFileFromGame(BootFlowPakFileName);
        ExtractPakFileFromGame(PhysicsPersistentPakFileName);
        CreateEmptyFile(ExtractedPakPath($"{PhysicsPersistentPakFileName}{BaseInstaller.RemoveFileSuffix}"));
        File.Copy(Path.Combine(pakPath, BootSplashPakFileName), ExtractedPakPath(BootFlowPakFileName));
        DeleteFromExtractedFiles(BmtFilesWildcard);
    }

    private void ExtractPakFileFromGame(string fileName)
    {
        var filePath = Path.Combine(pakPath, fileName);
        BPakFileEncryption.SetKeyset(KeysetType.PC2AndAbove);
        using var pakFile = BPakFile.FromFile(filePath, withExtraInfo: true, outputWriter: TextWriter.Null);
        pakFile.UnpackAll(Source.FullName);
    }

    private string ExtractedPakPath(string name) =>
        Path.Combine(Source.FullName, PakfilesDirectory, name);

    private void CreateEmptyFile(string path)
    {
        CreateParentDirectory(path);
        File.Create(path).Close();
    }

    private void CreateParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is not null)
            Directory.CreateDirectory(parent);
    }

    private void DeleteFromExtractedFiles(string wildcardRelative)
    {
        foreach (var file in Directory.EnumerateFiles(Source.FullName, wildcardRelative))
        {
            File.Delete(file);
        }
    }

    #endregion
}
