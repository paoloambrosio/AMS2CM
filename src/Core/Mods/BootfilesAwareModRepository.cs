using System.Collections.Immutable;
using Core.Mods;

internal class BootfilesAwareModRepository : IModRepository
{
    private const string BootfilesPrefix = "__bootfiles";

    private readonly IModRepository modRepository;

    public BootfilesAwareModRepository(IModRepository modRepository)
    {
        this.modRepository = modRepository;
    }

    public ModPackage UploadPackage(string sourceFilePath) =>
        modRepository.UploadPackage(sourceFilePath);

    public string EnablePackage(string packagePath) =>
        modRepository.EnablePackage(packagePath);

    public string DisablePackage(string packagePath) =>
        modRepository.DisablePackage(packagePath);

    public IReadOnlyCollection<ModPackage> ListPackages() =>
        modRepository.ListPackages()
            .Where(p => !IsBootFiles(p))
            .ToImmutableList();

    private bool IsBootFiles(ModPackage modPackage) =>
        modPackage.PackageName.StartsWith(BootfilesPrefix);
}
