using System.IO.Abstractions;
using Core.Packages.Installation.Installers;
using Core.Packages.Installation;
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
    private IFileInfo[]? sources;
    private bool prepared;
    internal void SelectSources(IReadOnlyDictionary<string, PackageInstallationState> state)
    {
        sources ??= [.. new[] { BootFlowPakFileName, BootSplashPakFileName, PhysicsPersistentPakFileName }
            .Select(name =>
            {
                var relative = Path.Combine(PakfilesDirectory, name);
                var live = FileSystem.FileInfo.New(Path.Combine(pakPath, name));
                var owner = state.Values.FirstOrDefault(s => s.Files.Any(p =>
                    StringComparer.OrdinalIgnoreCase.Equals(Paths.NormalizeDirectorySeparators(p), relative)));
                var source = owner is not null && (!live.Exists || live.CreationTimeUtc <= owner.Time.UtcDateTime)
                    ? FileSystem.FileInfo.New(live.FullName + ".orig") : live;
                return source.Exists ? source
                    : throw new FileNotFoundException("Missing bootfiles generation source", source.FullName);
            })];

    }

    internal void Prepare()
    {
        if (prepared) return;
        SelectSources(new Dictionary<string, PackageInstallationState>());
        GenerateBootfiles();
        prepared = true;
    }

    private readonly string BmtFilesWildcard =
        Path.Combine("vehicles", "_data", "effects", "backfire", "*.bmt");

    public GeneratedBootfilesInstaller(string packageName, IDirectoryInfo gameInstallationDirectory,
        IDirectoryInfo tempDir) :
        base(CreateExtractionDirectory(tempDir), packageName, null)
    {
        pakPath = Path.Combine(gameInstallationDirectory.FullName, PakfilesDirectory);
    }

    private static IDirectoryInfo CreateExtractionDirectory(IDirectoryInfo tempDir) =>
        tempDir.CreateSubdirectory(Guid.NewGuid().ToString());

    protected override void InstallAllFiles(InstallBody body)
    {
        Prepare();
        base.InstallAllFiles(body);
    }

    protected override void InstallFile(RootedPath destinationPath, IFileInfo fileInfo)
    {
        fileInfo.MoveTo(destinationPath.Full, overwrite: true);
    }

    private void GenerateBootfiles()
    {
        ExtractPakFileFromGame(BootFlowPakFileName);
        ExtractPakFileFromGame(PhysicsPersistentPakFileName);
        CreateEmptyFile(ExtractedPakPath($"{PhysicsPersistentPakFileName}{BaseInstaller.RemoveFileSuffix}"));
        FileSystem.File.Copy(SourceFile(BootSplashPakFileName), ExtractedPakPath(BootFlowPakFileName));
        DeleteFromExtractedFiles(BmtFilesWildcard);
    }

    private void ExtractPakFileFromGame(string fileName)
    {
        var filePath = SourceFile(fileName);
        BPakFileEncryption.SetKeyset(KeysetType.PC2AndAbove);
        using var pakFile = BPakFile.FromFile(filePath, withExtraInfo: true, outputWriter: TextWriter.Null);
        pakFile.UnpackAll(Source.FullName);
    }

    private string ExtractedPakPath(string name) =>
        Path.Combine(Source.FullName, PakfilesDirectory, name);

    private string SourceFile(string name) =>
        (sources ?? throw new InvalidOperationException("Generation sources have not been selected"))
        .Single(f => Path.GetFileName(f.FullName).RemoveSuffix(".orig") == name).FullName;

    private void CreateEmptyFile(string path)
    {
        CreateParentDirectory(path);
        FileSystem.File.WriteAllBytes(path, Array.Empty<byte>());
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
}
