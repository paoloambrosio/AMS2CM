using System.IO.Abstractions;
using Core.API;
using Core.Mods;
using Core.Mods.Installation;
using Core.Mods.Installation.Installers;
using Core.Packages;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Tests.Base;
using Core.Tests.Packages.Installation.Installers;
using FluentAssertions;
using static Core.Mods.Installation.Installers.GeneratedBootfilesInstaller;

namespace Core.Tests.Mods.Installation;

[IntegrationTest]
public class GeneratedBootfilesReconciliationTest : AbstractFilesystemTest
{
    private readonly IFileSystem fs = new FileSystem();
    private readonly ModInstallConfig config = new() { GenerateModDetails = false };
    private readonly PrefixBootfilesNaming naming;
    private readonly ModReconciliationService<PackageReconciliationService.IEventHandler> service;
    private readonly Mock<PackageReconciliationService.IEventHandler> events = new();
    private readonly List<(string Path, string Contents)> extracted = [];
    private IReadOnlyDictionary<string, PackageInstallationState> state = new Dictionary<string, PackageInstallationState>();
    private bool failExtraction;
    private string Game => TestPath("Game").Full;
    private string Temp => TestPath("Temp").Full;
    private string Pak(string name) => Path.Combine(Game, PakfilesDirectory, name);

    public GeneratedBootfilesReconciliationTest()
    {
        naming = new(config);
        var provider = new SkipUpdatedBackupStrategy.Provider<PackageReconciliationService.IEventHandler>(
            new SuffixBackupStrategy.Provider<DateTimeOffset, PackageReconciliationService.IEventHandler>());
        var factory = new Mock<IModInstallerFactory<PackageReconciliationService.IEventHandler>>();
        factory.Setup(f => f.ModInstaller(It.IsAny<IPackageInstaller>(), It.IsAny<IPackageInstaller>()))
            .Returns((IPackageInstaller inner, IPackageInstaller bootfiles) =>
                new ModInstaller(fs, inner, Temp, config, Game, bootfiles.PackageName));
        factory.Setup(f => f.BootfilesInstaller(It.IsAny<IPackageInstaller>(),
                It.IsAny<PackageReconciliationService.IEventHandler>(), It.IsAny<PackageInstallationState>()))
            .Returns((IPackageInstaller? inner, PackageReconciliationService.IEventHandler _, PackageInstallationState? previous) =>
                new BootfilesInstaller(fs, inner ?? new GeneratedBootfilesInstaller(fs, naming.GeneratedBootfilesName,
                        Game, Temp, Extract, previous), Temp, config, Game, naming,
                    Mock.Of<BootfilesInstaller.IEventHandler>(), previous));
        service = new(provider, naming, factory.Object, fs);
        foreach (var name in new[] { BootFlowPakFileName, BootSplashPakFileName, PhysicsPersistentPakFileName })
            CreateFile(new(Game, Path.Combine(PakfilesDirectory, name)), name);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Reconcile_UsesSelectedOriginalsBeforeUninstallAndKeepsPreparedOutput(
        bool previouslyInstalled, bool gameUpdated)
    {
        if (previouslyInstalled) Apply(false);
        if (gameUpdated) File.WriteAllText(Pak(BootFlowPakFileName), "Updated game bootflow");
        extracted.Clear();

        Apply(true);

        extracted.Select(file => file.Contents).Should().Equal(
            gameUpdated ? "Updated game bootflow" : BootFlowPakFileName, PhysicsPersistentPakFileName);
        extracted[1].Path.Should().Be(Pak(PhysicsPersistentPakFileName) + ".orig");
        File.Exists(Pak(PhysicsPersistentPakFileName)).Should().BeFalse();
        File.ReadAllText(Path.Combine(Game, "vehicles", "Bootflow.txt"))
            .Should().Be(gameUpdated ? "Updated game bootflow" : BootFlowPakFileName);
        var version = state[naming.GeneratedBootfilesName].VersionHash;

        Apply(true);

        extracted.Should().HaveCount(2, "unchanged bootfiles must not be generated again");
        state[naming.GeneratedBootfilesName].VersionHash.Should().Be(version);
        service.Reconcile(state, [], Game, current => state = current, events.Object, CancellationToken.None);
        state.Should().BeEmpty();
        File.ReadAllText(Pak(BootFlowPakFileName)).Should().Be(gameUpdated ? "Updated game bootflow" : BootFlowPakFileName);
        File.ReadAllText(Pak(PhysicsPersistentPakFileName)).Should().Be(PhysicsPersistentPakFileName);
        Directory.GetFiles(Game, "*", SearchOption.AllDirectories).Should().HaveCount(3);
    }

    [Fact]
    public void Reconcile_ExtractionFailurePreservesTheExistingInstallation()
    {
        Apply(false);
        var before = Directory.GetFiles(Game, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText);
        var previous = state[naming.GeneratedBootfilesName];
        File.WriteAllText(Pak(BootSplashPakFileName), "Updated splash");
        before[Pak(BootSplashPakFileName)] = "Updated splash";
        failExtraction = true;
        events.Invocations.Clear();

        var apply = () => Apply(false);

        apply.Should().Throw<IOException>().WithMessage("Extraction failed");
        events.Verify(e => e.UninstallingPackage(naming.GeneratedBootfilesName), Times.Never);
        state[naming.GeneratedBootfilesName].Should().BeEquivalentTo(previous);
        Directory.GetFiles(Game, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText).Should().BeEquivalentTo(before);
        failExtraction = false;
        Apply(false);
        state[naming.GeneratedBootfilesName].Partial.Should().BeFalse();
    }

    private void Extract(string source, string destination)
    {
        if (failExtraction) throw new IOException("Extraction failed");
        var contents = File.ReadAllText(source);
        extracted.Add((source, contents));
        var fileName = source.Contains(BootFlowPakFileName, StringComparison.OrdinalIgnoreCase)
            ? "Bootflow.txt" : "Physics.txt";
        CreateFile(new(destination, Path.Combine("vehicles", fileName)), contents);
        Directory.CreateDirectory(Path.Combine(destination, "vehicles", "_data", "effects", "backfire"));
    }

    private void Apply(bool removePhysics)
    {
        IPackage Package(string name, string path, string contents)
        {
            var package = new Mock<IPackage>();
            package.Setup(p => p.CreateInstaller()).Returns(() => new StaticFilesInstaller(fs, TimeProvider.System,
                name, 1, new Dictionary<string, string> { [path] = contents }, []));
            return package.Object;
        }
        var dependent = Package("Track", Path.Combine("tracks", "Example.trd"), "Track");
        IPackage[] packages = removePhysics
            ? [Package("Removal", Path.Combine(PakfilesDirectory, PhysicsPersistentPakFileName + "-remove"), ""), dependent]
            : [dependent];
        service.Reconcile(state, packages, Game, current => state = current, events.Object, CancellationToken.None);
    }
}
