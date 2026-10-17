using Core.Mods;
using System.IO.Abstractions.TestingHelpers;
using Core.Packages;
using Core.Mods.Installation;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Tests.Packages.Installation;
using Core.Utils;
using FluentAssertions;

namespace Core.Tests.Mods.Installation;

[IntegrationTest]
public class ModReconciliationServiceTest :
    ReconciliationServiceTestBase<PackageReconciliationService.IEventHandler>,
    IModInstallerFactory<PackageReconciliationService.IEventHandler>
{
    #region Setup

    private static readonly string GeneratedBootfilesName = "__generated";
    private static readonly string BootfilesPackageName = "__package";
    private static readonly string ModRequiringBootfiles = "ModRequiringBootfiles";
    private readonly List<string> configured = [];
    private bool failConfiguration;

    protected override IReconciliationService<PackageReconciliationService.IEventHandler> NewService(
        IBackupStrategyProvider<DateTimeOffset, PackageReconciliationService.IEventHandler> backupStrategyProvider)
    {
        var bootfilesNamingMock = new Mock<IBootfilesNaming>();
        bootfilesNamingMock.Setup(m => m.IsBootfiles(BootfilesPackageName)).Returns(true);
        bootfilesNamingMock.Setup(m => m.IsBootfiles(GeneratedBootfilesName)).Returns(true);
        bootfilesNamingMock.Setup(m => m.IsGeneratedBootfiles(GeneratedBootfilesName)).Returns(true);
        return new ModReconciliationService<PackageReconciliationService.IEventHandler>(
            backupStrategyProvider, bootfilesNamingMock.Object, this);
    }

    public IPackageInstaller ModInstaller(IPackageInstaller packageInstaller, IPackageInstaller bootfilesInstaller) =>
        packageInstaller.PackageName == ModRequiringBootfiles
            ? new InstallerWithBootfilesDependency(packageInstaller, bootfilesInstaller)
            : packageInstaller;

    public IBootfilesInstaller BootfilesInstaller(IPackageInstaller? bootfilesPackageInstaller, PackageReconciliationService.IEventHandler eventHandler) =>
        new TestBootfilesInstaller(bootfilesPackageInstaller ?? InstallerOf(GeneratedBootfilesName), () =>
        {
            if (failConfiguration) throw new IOException("Configuration failed");
            configured.Add(bootfilesPackageInstaller?.PackageName ?? GeneratedBootfilesName);
        });

    private class TestBootfilesInstaller(IPackageInstaller inner, Action configure) : IBootfilesInstaller
    {
        public string PackageName => inner.PackageName;
        public int? PackageVersionHash => inner.PackageVersionHash;
        public IReadOnlySet<string> PackageDependencies => inner.PackageDependencies;
        public IReadOnlySet<RootedPath> InstalledFiles => inner.InstalledFiles;
        public IInstallation.State Installed => inner.Installed;
        public DateTimeOffset InstallTime => inner.InstallTime;
        public IEnumerable<string> RelativeDirectoryPaths => inner.RelativeDirectoryPaths;
        public void Install(IInstaller.Destination destination, IBackupStrategy backupStrategy,
            ProcessingCallbacks<RootedPath> callbacks) => inner.Install(destination, backupStrategy, callbacks);
        public void Configure(IBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks) => configure();
        public void NotRequired() { }
    }

    private class InstallerWithBootfilesDependency(IPackageInstaller inner, IPackageInstaller bootfiles) : IPackageInstaller
    {
        public string PackageName => inner.PackageName;
        public int? PackageVersionHash => inner.PackageVersionHash;
        public IReadOnlySet<string> PackageDependencies =>
            inner.PackageDependencies.Append(bootfiles.PackageName).ToHashSet();
        public IReadOnlySet<RootedPath> InstalledFiles => inner.InstalledFiles;
        public IInstallation.State Installed => inner.Installed;
        public DateTimeOffset InstallTime => inner.InstallTime;

        public void Install(IInstaller.Destination destination, IBackupStrategy backupStrategy,
            ProcessingCallbacks<RootedPath> callbacks) => inner.Install(destination, backupStrategy, callbacks);
        public IEnumerable<string> RelativeDirectoryPaths => inner.RelativeDirectoryPaths;
    }

    #endregion


    [Fact]
    public void Apply_InstallsGeneratedBootfilesIfModRequiresThem()
    {
        Apply([
            InstallerOf(ModRequiringBootfiles)
        ]);

        InstalledPackages.Should().BeEquivalentTo([ModRequiringBootfiles, GeneratedBootfilesName]);
    }

    [Fact]
    public void Apply_InstallsBootfilesPackageIfModRequiresThem()
    {
        Apply([
            InstallerOf(ModRequiringBootfiles),
            InstallerOf(BootfilesPackageName)
        ]);

        InstalledPackages.Should().BeEquivalentTo([ModRequiringBootfiles, BootfilesPackageName]);
    }

    [Fact]
    public void Apply_DoesNotInstallBootfilesPackageWithoutDependents()
    {
        var packages = new List<string>();
        var progress = new List<double>();
        EventHandlerMock.Setup(m => m.InstallingPackage(It.IsAny<string>())).Callback<string>(packages.Add);
        EventHandlerMock.Setup(m => m.ProgressUpdate(It.IsAny<IPercent>()))
            .Callback<IPercent>(p => progress.Add(p.Percent));

        Apply([
            InstallerOf("I1"),           // 25%
            InstallerOf("I2"),           // 50%
            InstallerOf("I3"),           // 75%
            InstallerOf(BootfilesPackageName) // 100%
        ]);

        packages.Should().NotContain(BootfilesPackageName);
        progress.Should().Equal(0.25, 0.5, 0.75, 1.0);
    }

    [Fact]
    public void Apply_DoesNotInstallGeneratedBootfilesWithoutDependents()
    {
        var packages = new List<string>();
        var progress = new List<double>();
        EventHandlerMock.Setup(m => m.InstallingPackage(It.IsAny<string>())).Callback<string>(packages.Add);
        EventHandlerMock.Setup(m => m.ProgressUpdate(It.IsAny<IPercent>()))
            .Callback<IPercent>(p => progress.Add(p.Percent));

        Apply([
            // Generated bootfiles 100%
        ]);

        packages.Should().BeEmpty();
        progress.Should().Equal(1.0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Apply_ReconfiguresWithoutReinstallWhenOnlyDependentModsChange(int modVersion)
    {
        Apply([InstallerOf(ModRequiringBootfiles, 1), InstallerOf(BootfilesPackageName, 42)]);
        var installedAt = InstallationState![BootfilesPackageName].Time;
        EventHandlerMock.Invocations.Clear();
        configured.Clear();

        Apply([InstallerOf(ModRequiringBootfiles, modVersion), InstallerOf(BootfilesPackageName, 42)]);

        configured.Should().Equal(BootfilesPackageName);
        EventHandlerMock.Verify(e => e.UninstallingPackage(BootfilesPackageName), Times.Never);
        EventHandlerMock.Verify(e => e.InstallingPackage(BootfilesPackageName), Times.Never);
        InstallationState![BootfilesPackageName].Time.Should().Be(installedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(41)]
    public void Apply_ReinstallsWhenBootfilesVersionChangesOrWasUnknown(int? oldVersion)
    {
        Apply([InstallerOf(ModRequiringBootfiles, 1), InstallerOf(BootfilesPackageName, oldVersion)]);
        EventHandlerMock.Invocations.Clear();
        configured.Clear();

        Apply([InstallerOf(ModRequiringBootfiles, 1), InstallerOf(BootfilesPackageName, 42)]);

        EventHandlerMock.Verify(e => e.UninstallingPackage(BootfilesPackageName), Times.Once);
        EventHandlerMock.Verify(e => e.InstallingPackage(BootfilesPackageName), Times.Once);
        configured.Should().Equal(BootfilesPackageName);
        InstallationState![BootfilesPackageName].VersionHash.Should().Be(42);
    }

    [Fact]
    public void Apply_RemovesBootfilesWhenLastDependentIsRemovedAndNothingIsAdded()
    {
        Apply([InstallerOf(ModRequiringBootfiles, 1), InstallerOf(BootfilesPackageName, 42)]);
        configured.Clear();
        EventHandlerMock.Invocations.Clear();

        Apply([]);

        InstallationState.Should().BeEmpty();
        configured.Should().BeEmpty();
        EventHandlerMock.Verify(e => e.UninstallingPackage(BootfilesPackageName), Times.Once);
        EventHandlerMock.Verify(e => e.InstallingPackage(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Apply_RemovesSelectedBootfilesWhenDependentBecomesIndependent()
    {
        Apply([InstallerOf(ModRequiringBootfiles, 1), InstallerOf(BootfilesPackageName, 42)]);
        configured.Clear();

        Apply([InstallerOf("Independent", 1), InstallerOf(BootfilesPackageName, 42)]);

        InstalledPackages.Should().Equal("Independent");
        configured.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(0, true)]
    public void Apply_ReinstallsIfExistingShadowerChangesOrIsRemoved(int shadowerVersion, bool reinstall)
    {
        Apply([InstallerOf(ModRequiringBootfiles, 1), InstallerOf("Shadow", 1, ["Shared"]),
            InstallerOf(BootfilesPackageName, 42, ["Shared", "Boot"])]);
        EventHandlerMock.Invocations.Clear();
        configured.Clear();
        var installers = new List<IPackageInstaller> { InstallerOf(ModRequiringBootfiles, 1) };
        if (shadowerVersion != 0) installers.Add(InstallerOf("Shadow", shadowerVersion, ["Shared"]));
        installers.Add(InstallerOf(BootfilesPackageName, 42, ["Shared", "Boot"]));

        Apply(installers.ToArray());

        EventHandlerMock.Verify(e => e.UninstallingPackage(BootfilesPackageName), reinstall ? Times.Once : Times.Never);
        configured.Should().Equal(BootfilesPackageName);
    }

    [Fact]
    public void Apply_ReinstallsIfNewModShadowsBootfiles()
    {
        Apply([InstallerOf(ModRequiringBootfiles, 1), InstallerOf(BootfilesPackageName, 42, ["Shared", "Boot"])]);
        EventHandlerMock.Invocations.Clear();

        Apply([InstallerOf(ModRequiringBootfiles, 1), InstallerOf("Shadow", 1, ["Shared"]),
            InstallerOf(BootfilesPackageName, 42, ["Shared", "Boot"])]);

        EventHandlerMock.Verify(e => e.UninstallingPackage(BootfilesPackageName), Times.Once);
        InstallationState![BootfilesPackageName].ShadowedBy.Should().Equal("Shadow");
        InstallationState[BootfilesPackageName].Files.Should().Equal("Boot");
    }

    [Fact]
    public void Apply_ConfigurationFailurePreservesStateAndRecovers()
    {
        Apply([InstallerOf(ModRequiringBootfiles, 1), InstallerOf(BootfilesPackageName, 42)]);
        var previous = InstallationState![BootfilesPackageName];
        failConfiguration = true;
        var reconcile = () => Apply([InstallerOf(ModRequiringBootfiles, 1), InstallerOf(BootfilesPackageName, 42)]);

        reconcile.Should().Throw<IOException>();
        InstallationState![BootfilesPackageName].Should().BeEquivalentTo(previous with { Partial = true });

        failConfiguration = false;
        reconcile();
        InstallationState![BootfilesPackageName].Partial.Should().BeFalse();
    }

    [Fact]
    public void Apply_DoesNotReadMissingGenerationInputsIfBootfilesAreUnneeded()
    {
        Apply([new GeneratedBootfilesInstaller(new MockFileSystem(), GeneratedBootfilesName, "Game", "Temp")]);
        InstalledPackages.Should().BeEmpty();
        configured.Should().BeEmpty();
    }

    [Fact]
    public void Apply_MissingGenerationInputsFailBeforeUninstall()
    {
        InstallationState = new Dictionary<string, PackageInstallationState>
        {
            [GeneratedBootfilesName] = new(TestInstallationTimeUtc, 42, false, [], ["Boot"], []),
            [ModRequiringBootfiles] = new(TestInstallationTimeUtc, 1, false, [GeneratedBootfilesName], ["Mod"], [])
        };
        var reconcile = () => Apply([InstallerOf(ModRequiringBootfiles, 1),
            new GeneratedBootfilesInstaller(new MockFileSystem(), GeneratedBootfilesName, "Game", "Temp")]);

        reconcile.Should().Throw<FileNotFoundException>();

        EventHandlerMock.Verify(e => e.UninstallingPackage(GeneratedBootfilesName), Times.Never);
        InstallationState![GeneratedBootfilesName].Files.Should().Equal("Boot");
    }

    [Fact]
    public void Apply_CancellationBeforeConfigurationResumesWithoutReinstallation()
    {
        using var cancellation = new CancellationTokenSource();
        EventHandlerMock.Setup(e => e.InstallingPackage(BootfilesPackageName)).Callback(cancellation.Cancel);
        var packages = new[] { InstallerOf(ModRequiringBootfiles, 1), InstallerOf(BootfilesPackageName, 42) }
            .Select(installer =>
            {
                var package = new Mock<IPackage>();
                package.Setup(p => p.CreateInstaller()).Returns(installer);
                return package.Object;
            }).ToArray();
        var provider = new Mock<IBackupStrategyProvider<DateTimeOffset, PackageReconciliationService.IEventHandler>>();
        provider.Setup(p => p.BackupStrategy(It.IsAny<DateTimeOffset>(), It.IsAny<PackageReconciliationService.IEventHandler>()))
            .Returns(BackupStrategyMock.Object);

        NewService(provider.Object).Reconcile(new Dictionary<string, PackageInstallationState>(), packages,
            DestinationPath("").Root, current => InstallationState = current, EventHandlerMock.Object, cancellation.Token);

        configured.Should().BeEmpty();
        InstallationState.Should().ContainKey(BootfilesPackageName);
        EventHandlerMock.Invocations.Clear();
        Apply([InstallerOf(ModRequiringBootfiles, 1), InstallerOf(BootfilesPackageName, 42)]);
        configured.Should().Equal(BootfilesPackageName);
        EventHandlerMock.Verify(e => e.InstallingPackage(BootfilesPackageName), Times.Never);
    }

    [Fact]
    public void Apply_AlignsBootfilesDepsWithCurrent()
    {
        InstallationState = new Dictionary<string, PackageInstallationState>
        {
            [ModRequiringBootfiles] = new(Time: ValueNotUsed, VersionHash: 1, Partial: false,
                Dependencies: [BootfilesPackageName], Files: FilesNotUsed, ShadowedBy: []),
            ["Untouched"] = new(Time: ValueNotUsed, VersionHash: 2, Partial: false,
                Dependencies: [BootfilesPackageName], Files: ["F2"], ShadowedBy: []),
            [BootfilesPackageName] = new(Time: ValueNotUsed, VersionHash: 1, Partial: false,
                Dependencies: [], Files: FilesNotUsed, ShadowedBy: [])
        };

        Apply([
            InstallerOf(ModRequiringBootfiles, versionHash: 3, files: ["F3"]),
            InstallerOf("Untouched", versionHash: 2, files: FilesNotUsed)
        ]);

        EventHandlerMock.Verify(m => m.UninstallingPackage(ModRequiringBootfiles));
        EventHandlerMock.Verify(m => m.InstallingPackage(ModRequiringBootfiles));
        EventHandlerMock.Verify(m => m.SkippingPackage("Untouched"));
        EventHandlerMock.Verify(m => m.InstallingPackage(GeneratedBootfilesName));

        InstallationState.Keys.Should().BeEquivalentTo(ModRequiringBootfiles, "Untouched", GeneratedBootfilesName);
        InstallationState[ModRequiringBootfiles].Dependencies.Should().BeEquivalentTo([GeneratedBootfilesName]);
        InstallationState["Untouched"].Dependencies.Should().BeEquivalentTo([GeneratedBootfilesName]);
    }
}
