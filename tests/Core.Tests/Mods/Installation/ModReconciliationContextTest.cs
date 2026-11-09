using Core.API;
using Core.Games;
using Core.Mods;
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
public class ModReconciliationContextTest :
    ReconciliationIntegrationTestBase<ModReconciliationContext.IEventHandler>
{
    #region Setup

    private readonly Mock<Action> configure = new();
    private Mock<Action> configureMod = new();

    private const string GeneratedBootfilesName = "__generated";
    private const string BootfilesPackageName = "__package";
    private const string ModRequiringBootfiles = "ModRequiringBootfiles";

    protected override IReconciliationContext<ModReconciliationContext.IEventHandler>.FactoryFunc ContextFactory
    {
        get
        {
            var bootfilesNamingMock = new Mock<IBootfilesNaming>();
            bootfilesNamingMock.Setup(m => m.IsBootfiles(BootfilesPackageName)).Returns(true);
            bootfilesNamingMock.Setup(m => m.IsGeneratedBootfiles(GeneratedBootfilesName)).Returns(true);
            return previousState => new TestModReconciliationContext(previousState, bootfilesNamingMock.Object, this);
        }
    }

    #endregion

    [Fact]
    public void RetainedModIsConfiguredAfterKeep()
    {
        Reconcile([Package("Mod", 1)]);
        configureMod = new Mock<Action>(MockBehavior.Strict);
        var sequence = new MockSequence();
        var events = new Mock<ModReconciliationContext.IEventHandler>(MockBehavior.Strict);
        events.InSequence(sequence).Setup(e => e.SkippingPackage("Mod"));
        configureMod.InSequence(sequence).Setup(c => c());

        Reconcile([Package("Mod", 1)], events.Object);

        configureMod.Verify(c => c(), Times.Once);
        events.Verify(e => e.SkippingPackage("Mod"), Times.Once);
    }

    [Fact]
    public void RetainedModWithReusedInstallerIsReconfigured()
    {
        var package = Package("Mod", 1);
        Reconcile([package]);
        configureMod.Invocations.Clear();

        Reconcile([package]);

        configureMod.Verify(c => c(), Times.Once);
    }

    [Fact]
    public void NewModIsConfiguredOnce()
    {
        Reconcile([Package("Mod", 1)]);

        configureMod.Verify(c => c(), Times.Once);
    }

    [Fact]
    public void ReplacedModIsConfiguredOnce()
    {
        Reconcile([Package("Mod", 1)]);
        configureMod.Invocations.Clear();

        Reconcile([Package("Mod", 2)]);

        configureMod.Verify(c => c(), Times.Once);
    }

    [Fact]
    public void RemovedModIsNotConfigured()
    {
        Reconcile([Package("Mod", 1)]);
        configureMod.Invocations.Clear();

        Reconcile([]);

        configureMod.Verify(c => c(), Times.Never);
    }

    [Fact]
    public void FailedKeepDoesNotConfigureMod()
    {
        Reconcile([Package("Mod", 1)]);
        configureMod.Invocations.Clear();
        EventHandlerMock.Setup(e => e.SkippingPackage("Mod")).Throws(new IOException("Keep failed"));

        var reconcile = () => Reconcile([Package("Mod", 1)]);

        reconcile.Should().Throw<IOException>().WithMessage("Keep failed");
        configureMod.Verify(c => c(), Times.Never);
    }

    [Fact]
    public void Reconcile_InstallsGeneratedBootfilesIfModRequiresThem()
    {
        Reconcile([
            Package(ModRequiringBootfiles)
        ]);

        InstalledPackages.Should().BeEquivalentTo([ModRequiringBootfiles, GeneratedBootfilesName]);
    }

    [Fact]
    public void Reconcile_InstallsBootfilesPackageIfModRequiresThem()
    {
        Reconcile([
            Package(ModRequiringBootfiles),
            Package(BootfilesPackageName)
        ]);

        InstalledPackages.Should().BeEquivalentTo(ModRequiringBootfiles, BootfilesPackageName);
    }

    [Fact]
    public void Reconcile_SkipsUnnecessaryBootfilesPackage()
    {
        var packages = new List<string>();
        EventHandlerMock.Setup(m => m.InstallingPackage(It.IsAny<string>())).Callback<string>(packages.Add);

        Reconcile([
            Package("I1"),
            Package("I2"),
            Package("I3"),
            Package(BootfilesPackageName)
        ]);

        packages.Should().NotContain(BootfilesPackageName);
    }

    [Fact]
    public void Reconcile_SkipsUnnecessaryGeneratedBootfiles()
    {
        var packages = new List<string>();
        EventHandlerMock.Setup(m => m.InstallingPackage(It.IsAny<string>())).Callback<string>(packages.Add);

        Reconcile([]);

        packages.Should().BeEmpty();
    }

    [Fact]
    public void Reconcile_AlignsBootfilesDependenciesWithCurrent()
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
        var previousState = InstallationState;

        Reconcile([
            Package(ModRequiringBootfiles, versionHash: 3, files: ["F3"]),
            Package("Untouched", versionHash: 2, files: FilesNotUsed, dependencies: [GeneratedBootfilesName])
        ]);

        EventHandlerMock.Verify(m => m.UninstallingPackage(ModRequiringBootfiles));
        EventHandlerMock.Verify(m => m.InstallingPackage(ModRequiringBootfiles));
        EventHandlerMock.Verify(m => m.SkippingPackage("Untouched"));
        EventHandlerMock.Verify(m => m.InstallingPackage(GeneratedBootfilesName));

        InstallationState.Keys.Should().BeEquivalentTo(ModRequiringBootfiles, "Untouched", GeneratedBootfilesName);
        InstallationState[ModRequiringBootfiles].Dependencies.Should().BeEquivalentTo(GeneratedBootfilesName);
        InstallationState["Untouched"].Dependencies.Should().BeEquivalentTo(GeneratedBootfilesName);

        previousState["Untouched"].Dependencies.Should().BeEquivalentTo(BootfilesPackageName);
    }

    [Fact]
    public void Reconcile_PlanningPreservesUnprocessedBootfilesDependencies()
    {
        InstallationState = new Dictionary<string, PackageInstallationState>
        {
            ["Untouched"] = new(Time: ValueNotUsed, VersionHash: 1, Partial: false,
                Dependencies: [BootfilesPackageName], Files: FilesNotUsed, ShadowedBy: [])
        };
        var previousState = InstallationState;

        Reconcile([
            Package("Untouched", versionHash: 1)
        ], actionCount: 0);

        InstallationState["Untouched"].Dependencies.Should().Equal(BootfilesPackageName);
        previousState["Untouched"].Dependencies.Should().Equal(BootfilesPackageName);
    }
    [Fact]
    public void RetainedBootfilesAreConfiguredAfterDependentModChanges()
    {
        Reconcile([Package(ModRequiringBootfiles, 1), Package(BootfilesPackageName, 1)]);
        var originalTime = InstallationState[BootfilesPackageName].Time;
        EventHandlerMock.Invocations.Clear();

        Reconcile([Package(ModRequiringBootfiles, 2), Package(BootfilesPackageName, 1)]);

        EventHandlerMock.Verify(e => e.SkippingPackage(BootfilesPackageName), Times.Once);
        EventHandlerMock.Verify(e => e.UninstallingPackage(BootfilesPackageName), Times.Never);
        configure.Verify(c => c(), Times.Exactly(2));
        InstallationState[BootfilesPackageName].Time.Should().Be(originalTime);
    }

    [Fact]
    public void BootfilesReplacementCompletesConfiguration()
    {
        Reconcile([Package(ModRequiringBootfiles, 1), Package(BootfilesPackageName, 1)]);
        var sequence = new MockSequence();
        var events = new Mock<ModReconciliationContext.IEventHandler>(MockBehavior.Strict);
        events.InSequence(sequence).Setup(e => e.SkippingPackage(ModRequiringBootfiles));
        events.InSequence(sequence).Setup(e => e.UninstallingPackage(BootfilesPackageName));
        events.InSequence(sequence).Setup(e => e.InstallingPackage(BootfilesPackageName));
        configure.Invocations.Clear();
        configure.InSequence(sequence).Setup(c => c());

        Reconcile([Package(ModRequiringBootfiles, 1), Package(BootfilesPackageName, 2)], events.Object);

        configure.Verify(c => c(), Times.Once);
        InstallationState[BootfilesPackageName].VersionHash.Should().Be(2);
        InstallationState[BootfilesPackageName].Partial.Should().BeFalse();
    }

    [Fact]
    public void RemovingLastDependentRemovesBootfiles()
    {
        Reconcile([Package(ModRequiringBootfiles, 1), Package(BootfilesPackageName, 1)]);
        configure.Invocations.Clear();

        Reconcile([Package(BootfilesPackageName, 1)]);

        InstallationState.Should().BeEmpty();
        configure.Verify(c => c(), Times.Never);
    }

    [Fact]
    public void ConfigurationFailureRetainsPartialBootfilesState()
    {
        Reconcile([Package(ModRequiringBootfiles, 1), Package(BootfilesPackageName, 1)]);
        var original = InstallationState[BootfilesPackageName];
        configure.Setup(c => c()).Throws(new IOException("Configuration failed"));

        var reconcile = () => Reconcile([Package(ModRequiringBootfiles, 1), Package(BootfilesPackageName, 1)]);

        reconcile.Should().Throw<IOException>();
        InstallationState[BootfilesPackageName].Should().BeEquivalentTo(original with { Partial = true });
    }

    [Fact]
    public void UnknownBootfilesVersionIsReinstalledOnce()
    {
        Reconcile([Package(ModRequiringBootfiles, 1), Package(BootfilesPackageName, 1)]);
        var state = new Dictionary<string, PackageInstallationState>(InstallationState)
        {
            [BootfilesPackageName] = InstallationState[BootfilesPackageName] with { VersionHash = null }
        };
        InstallationState = state;
        EventHandlerMock.Invocations.Clear();

        Reconcile([Package(ModRequiringBootfiles, 1), Package(BootfilesPackageName, 1)]);
        Reconcile([Package(ModRequiringBootfiles, 1), Package(BootfilesPackageName, 1)]);

        EventHandlerMock.Verify(e => e.InstallingPackage(BootfilesPackageName), Times.Once);
        EventHandlerMock.Verify(e => e.SkippingPackage(BootfilesPackageName), Times.Once);
    }

    [Fact]
    public void ChangedShadowingReinstallsBootfiles()
    {
        Reconcile([
            Package(ModRequiringBootfiles, 1, ["Shared"]),
            Package(BootfilesPackageName, 1, ["Shared", "BootOnly"])
        ]);
        EventHandlerMock.Invocations.Clear();

        Reconcile([
            Package(ModRequiringBootfiles, 2, ["Other"]),
            Package(BootfilesPackageName, 1, ["Shared", "BootOnly"])
        ]);

        EventHandlerMock.Verify(e => e.InstallingPackage(BootfilesPackageName), Times.Once);
        InstallationState[BootfilesPackageName].Files.Should().Contain("Shared");
    }

    [Fact]
    public void NewClaimPreventsBootfilesUninstallFromRestoringPath()
    {
        Reconcile([
            Package(ModRequiringBootfiles, 1, ["Other"]),
            Package(BootfilesPackageName, 1, ["Shared", "BootOnly"])
        ]);
        BackupStrategyMock.Invocations.Clear();

        Reconcile([
            Package(ModRequiringBootfiles, 2, ["Shared"]),
            Package(BootfilesPackageName, 1, ["Shared", "BootOnly"])
        ]);

        BackupStrategyMock.Verify(b => b.RestoreBackup(DestinationPath("Shared")), Times.Never);
        InstallationState[BootfilesPackageName].Files.Should().NotContain("Shared");
        InstallationState[BootfilesPackageName].ShadowedBy.Should().Contain(ModRequiringBootfiles);
    }

    [Fact]
    public void FirstActionRemovesStaleBootfilesOwnership()
    {
        Reconcile([
            Package(ModRequiringBootfiles, 1, ["Other"]),
            Package(BootfilesPackageName, 1, ["Shared", "BootOnly"])
        ]);

        Reconcile([
            Package(ModRequiringBootfiles, 2, ["Shared"]),
            Package(BootfilesPackageName, 1, ["Shared", "BootOnly"])
        ], actionCount: 1);

        InstallationState[BootfilesPackageName].Files.Should().NotContain("Shared");
        InstallationState[BootfilesPackageName].ShadowedBy.Should().Contain(ModRequiringBootfiles);
    }

    #region Utility Methods

    private sealed class TestModReconciliationContext(
        IReadOnlyDictionary<string, PackageInstallationState> previousState,
        IBootfilesNaming bootfilesNaming,
        ModReconciliationContextTest test)
        : ModReconciliationContext<ModReconciliationContext.IEventHandler, ModInstallConfig>(previousState,
            Mock.Of<IGame>(), Mock.Of<ITempDir>(), bootfilesNaming, new ModInstallConfig())
    {
        protected override IReconfigurableInstaller CreateModInstaller(IPackageInstaller packageInstaller,
            IPackageInstaller bootfilesInstaller) =>
            new ConfigurableInstaller(new Dictionary<string, IPackageInstaller>
            {
                [ModRequiringBootfiles] = new InstallerWithBootfilesDependency(packageInstaller, bootfilesInstaller)
            }.GetValueOrDefault(packageInstaller.PackageName, packageInstaller), test.configureMod.Object);
        protected override IBootfilesInstaller CreateBootfilesInstaller(IPackageInstaller? bootfilesPackageInstaller,
            ModReconciliationContext.IEventHandler eventHandler) =>
            new TestBootfilesInstaller(bootfilesPackageInstaller ?? test.Installer(GeneratedBootfilesName), test.configure.Object);
    }

    private class InstallerWithBootfilesDependency(IPackageInstaller inner, IPackageInstaller bootfiles)
        : IPackageInstaller
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

    private class ConfigurableInstaller(IPackageInstaller inner, Action configure) : IReconfigurableInstaller
    {
        private IReadOnlySet<RootedPath>? ownedFiles;
        public string PackageName => inner.PackageName;
        public int? PackageVersionHash => inner.PackageVersionHash;
        public IReadOnlySet<string> PackageDependencies => inner.PackageDependencies;
        public IReadOnlySet<RootedPath> InstalledFiles => ownedFiles ?? inner.InstalledFiles;
        public IInstallation.State Installed { get; private set; } = IInstallation.State.NotInstalled;
        public DateTimeOffset InstallTime => inner.InstallTime;
        public IEnumerable<string> RelativeDirectoryPaths => inner.RelativeDirectoryPaths;
        public void Install(IInstaller.Destination destination, IBackupStrategy backupStrategy,
            ProcessingCallbacks<RootedPath> callbacks)
        {
            Installed = IInstallation.State.PartiallyInstalled;
            inner.Install(destination, backupStrategy, callbacks);
            configure();
            Installed = IInstallation.State.Installed;
        }
        public void Reconfigure(IReadOnlySet<RootedPath> installedFiles, IBackupStrategy backupStrategy,
            ProcessingCallbacks<RootedPath> callbacks)
        {
            Installed = IInstallation.State.PartiallyInstalled;
            ownedFiles = installedFiles;
            configure();
            Installed = IInstallation.State.Installed;
        }
    }

    private sealed class TestBootfilesInstaller(IPackageInstaller inner, Action configure)
        : ConfigurableInstaller(inner, configure), IBootfilesInstaller
    {
        public void NotRequired() { }
        public void SelectSources(IReadOnlyDictionary<string, PackageInstallationState> state) { }
        public void Prepare(IReadOnlyDictionary<string, PackageInstallationState> state) { }
    }
    #endregion
}
