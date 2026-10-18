namespace Core.Packages.Installation.Installers;

public interface IPackageInstaller : IInstaller
{
    string PackageName { get; }
    int? PackageVersionHash { get; }
    int InstallationFormatVersion => 0;
    IReadOnlySet<string> PackageDependencies { get; }
}
