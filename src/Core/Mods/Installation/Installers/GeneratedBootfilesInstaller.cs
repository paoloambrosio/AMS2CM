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
    private readonly string BmtFilesWildcard =
        Path.Combine("vehicles", "_data", "effects", "backfire", "*.bmt");

    public GeneratedBootfilesInstaller(IFileSystem fileSystem, TimeProvider timeProvider, PackageMetadata package,
        InstallationPaths paths) :
        base(fileSystem, timeProvider, package)
    {
        pakPath = Path.Combine(paths.GameDirectory, PakfilesDirectory);
        var extractionPath = Path.Combine(paths.TempDirectory, Guid.NewGuid().ToString());
        Source = fileSystem.Directory.CreateDirectory(extractionPath);
    }

    protected override IDirectoryInfo Source { get; }

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
        BPakFileEncryption.SetKeyset(KeysetType.PC2AndAbove);
        using var pakFile = BPakFile.FromFile(filePath, withExtraInfo: true, outputWriter: TextWriter.Null);
        pakFile.UnpackAll(Source.FullName);
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
