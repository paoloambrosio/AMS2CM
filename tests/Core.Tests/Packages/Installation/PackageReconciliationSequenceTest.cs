using System.IO.Abstractions.TestingHelpers;
using System.IO.Abstractions;
using Core.API;
using Core.Mods.Installation.Installers;
using Core.State;
using Core.State.JsonFile;
using Core.Packages;
using Core.Packages.Installation;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Tests.Packages.Installation.Installers;
using Core.Utils;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Core.Tests.Packages.Installation;

[IntegrationTest]
public class PackageReconciliationSequenceTest
{
    private readonly MockFileSystem fs = new();
    private readonly FakeTimeProvider time = new(DateTimeOffset.UtcNow.AddDays(1));
    private readonly string installDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly Mock<PackageReconciliationService.IEventHandler> events = new();
    private readonly Mock<IBackupStrategy> backups = new();
    private readonly MoveFileBackupStrategy realBackups;
    private readonly ObservingService service;
    private IReadOnlyDictionary<string, PackageInstallationState> state =
        new Dictionary<string, PackageInstallationState>();

    public PackageReconciliationSequenceTest()
    {
        realBackups = new(fs, new BackupNaming(), null);
        backups.Setup(b => b.PerformBackup(It.IsAny<RootedPath>())).Callback<RootedPath>(realBackups.PerformBackup);
        backups.Setup(b => b.RestoreBackup(It.IsAny<RootedPath>())).Callback<RootedPath>(realBackups.RestoreBackup);
        backups.Setup(b => b.DeleteBackup(It.IsAny<RootedPath>())).Callback<RootedPath>(realBackups.DeleteBackup);
        backups.Setup(b => b.AfterInstall(It.IsAny<RootedPath>())).Callback<RootedPath>(realBackups.AfterInstall);
        var provider = new Mock<IBackupStrategyProvider<DateTimeOffset, PackageReconciliationService.IEventHandler>>();
        provider.Setup(p => p.BackupStrategy(It.IsAny<DateTimeOffset>(),
                It.IsAny<PackageReconciliationService.IEventHandler>()))
            .Returns((DateTimeOffset installedAt, PackageReconciliationService.IEventHandler handler) =>
                new SkipUpdatedBackupStrategy(fs, backups.Object, installedAt, handler));
        service = new(provider.Object, fs);
    }

    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 2, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 2, true)]
    [InlineData(true, 2, true)]
    public void Apply_TransfersOwnershipWithoutLosingTheFileOrOriginalBackup(
        bool originalExists, int nextVersion, bool dropsSharedFile)
    {
        if (originalExists)
        {
            fs.AddFile(PathOf("Shared"), "Original");
        }
        Apply([Installer("B", 1, ("Shared", "B1"), ("BF", "B1"))]);
        backups.Invocations.Clear();

        var b = dropsSharedFile
            ? Installer("B", nextVersion, ("BF", "B2"))
            : Installer("B", nextVersion, ("Shared", "B2"), ("BF", "B2"));
        Apply([Installer("A", 1, ("shared", "A")), b]);

        fs.File.ReadAllText(PathOf("Shared")).Should().Be("A");
        fs.File.ReadAllText(PathOf("BF")).Should().Be(nextVersion == 1 ? "B1" : "B2");
        state["A"].Files.Should().Equal("shared");
        state["B"].Files.Should().Equal("BF");
        if (!dropsSharedFile)
        {
            state["B"].ShadowedBy.Should().Equal("A");
        }
        AssertOriginalBackup(originalExists);
        backups.Verify(bk => bk.RestoreBackup(new RootedPath(installDir, "Shared")), Times.Never);

        Apply([]);

        state.Should().BeEmpty();
        AssertRestoredFilesystem(originalExists);
    }

    [Fact]
    public void Apply_LaterUninstallDoesNotResurrectARemovedFile()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("B", 1, ("Shared", "B"), ("BF", "B"))]);

        Apply([
            Installer("A", 1, ("Shared-remove", "")),
            Installer("B", 2, ("Shared", "B2"), ("BF", "B2"))
        ]);

        fs.File.Exists(PathOf("Shared")).Should().BeFalse();
        state["A"].Files.Should().Equal("Shared");
        state["B"].Files.Should().Equal("BF");
        AssertOriginalBackup(true);

        Apply([]);
        AssertRestoredFilesystem(true);
    }

    [Fact]
    public void Apply_PreservesShadowingAcrossKeptRunsAndExposesFilesAfterAnUpdate()
    {
        Apply([
            Installer("A", 1, ("Shared", "A")),
            Installer("B", 1, ("Shared", "B"), ("BF", "B"))
        ]);
        Apply([
            Installer("A", 1, ("Shared", "A")),
            Installer("B", 1, ("Shared", "B"), ("BF", "B"))
        ]);
        state["B"].ShadowedBy.Should().Equal("A");

        Apply([
            Installer("A", 2, ("AF", "A2")),
            Installer("B", 1, ("Shared", "B"), ("BF", "B"))
        ]);

        fs.File.ReadAllText(PathOf("Shared")).Should().Be("B");
        state["B"].Files.Should().BeEquivalentTo(["Shared", "BF"]);
        state["B"].ShadowedBy.Should().BeEmpty();
        Apply([]);
        fs.AllFiles.Should().BeEmpty();
    }

    [Fact]
    public void Apply_PriorityInversionAndDisabledOwnerRestoreTheRightContents()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([
            Installer("A", 1, ("Shared", "A"), ("AF", "A")),
            Installer("B", 1, ("Shared", "B"), ("BF", "B"))
        ]);
        Apply([
            Installer("B", 1, ("Shared", "B"), ("BF", "B")),
            Installer("A", 1, ("Shared", "A"), ("AF", "A"))
        ]);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("B");
        state["A"].ShadowedBy.Should().Equal("B");

        Apply([Installer("A", 1, ("Shared", "A"), ("AF", "A"))]);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("A");
        state["A"].ShadowedBy.Should().BeEmpty();
        AssertOriginalBackup(true);

        Apply([]);
        AssertRestoredFilesystem(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_CancellationPersistsTransferredOwnershipAndCanResume(bool fullyShadowed)
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([fullyShadowed
            ? Installer("B", 1, ("Shared", "B"))
            : Installer("B", 1, ("Shared", "B"), ("BF", "B"))]);
        using var cancellation = new CancellationTokenSource();
        events.Setup(e => e.ProgressUpdate(It.IsAny<IPercent>())).Callback(cancellation.Cancel);

        Apply([
            Installer("A", 1, ("Shared", "A")),
            Installer("B", 1, ("Shared", "B"), ("BF", "B"))
        ], cancellation.Token);

        state["A"].Files.Should().Equal("Shared");
        if (fullyShadowed)
        {
            state.Should().NotContainKey("B");
        }
        else
        {
            state["B"].Files.Should().Equal("BF");
            state["B"].ShadowedBy.Should().Equal("A");
            state["B"].Partial.Should().BeFalse();
        }
        AssertOriginalBackup(true);

        // Removing A on the next run must expose B's file, even though B never ran.
        Apply([Installer("B", 1, ("Shared", "B"), ("BF", "B"))]);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("B");
        Apply([]);
        AssertRestoredFilesystem(true);
    }

    [Fact]
    public void Apply_CanCancelBetweenUninstallAndReinstall()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("B", 1, ("Shared", "B"), ("BF", "B"))]);
        using var cancellation = new CancellationTokenSource();
        events.Setup(e => e.UninstallingPackage("B")).Callback(cancellation.Cancel);
        var progress = new List<double>();
        events.Setup(e => e.ProgressUpdate(It.IsAny<IPercent>()))
            .Callback<IPercent>(p => progress.Add(p.Percent));

        Apply([
            Installer("A", 1, ("Shared", "A")),
            Installer("B", 2, ("Shared", "B2"), ("BF", "B2"))
        ], cancellation.Token);

        progress.Should().Equal(0.5);
        state.Keys.Should().Equal("A");
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("A");
        fs.File.Exists(PathOf("BF")).Should().BeFalse();
        AssertOriginalBackup(true);

        Apply([
            Installer("A", 1, ("Shared", "A")),
            Installer("B", 2, ("Shared", "B2"), ("BF", "B2"))
        ]);
        fs.File.ReadAllText(PathOf("BF")).Should().Be("B2");
        Apply([]);
        AssertRestoredFilesystem(true);
    }

    [Fact]
    public void Apply_RestoreFailurePreservesTheNewOwnerAndRemainingOldFiles()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("B", 1, ("Shared", "B"), ("BF", "B"))]);
        var fail = true;
        backups.Setup(b => b.RestoreBackup(new RootedPath(installDir, "BF")))
            .Callback<RootedPath>(path =>
            {
                if (fail) throw new IOException("Restore failed");
                realBackups.RestoreBackup(path);
            });

        var reconcile = () => Apply([
            Installer("A", 1, ("Shared", "A")),
            Installer("B", 2, ("Shared", "B2"), ("BF", "B2"))
        ]);
        reconcile.Should().Throw<IOException>();

        state["A"].Files.Should().Equal("Shared");
        state["B"].Files.Should().Equal("BF");
        state["B"].Partial.Should().BeTrue();
        state["B"].ShadowedBy.Should().Equal("A");
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("A");
        AssertOriginalBackup(true);

        fail = false;
        reconcile();
        state["B"].Partial.Should().BeFalse();
        fs.File.ReadAllText(PathOf("BF")).Should().Be("B2");
        Apply([]);
        AssertRestoredFilesystem(true);
    }

    [Fact]
    public void Apply_InstallFailurePersistsOwnershipTransfersBeforeTheNextPackageRuns()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("B", 1, ("Shared", "B"), ("BF", "B"))]);
        var fail = true;
        backups.Setup(b => b.PerformBackup(new RootedPath(installDir, "Fail")))
            .Callback<RootedPath>(path =>
            {
                if (fail) throw new IOException("Backup failed");
                realBackups.PerformBackup(path);
            });
        var reconcile = () => Apply([
            Installer("A", 1, ("Shared", "A"), ("Fail", "A")),
            Installer("B", 1, ("Shared", "B"), ("BF", "B"))
        ]);

        reconcile.Should().Throw<IOException>();
        state["A"].Partial.Should().BeTrue();
        state["A"].Files.Should().BeEquivalentTo(["Shared"]);
        state["B"].Files.Should().Equal("BF");
        state["B"].ShadowedBy.Should().Equal("A");
        AssertOriginalBackup(true);

        fail = false;
        reconcile();
        state["A"].Partial.Should().BeFalse();
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("A");
        Apply([]);
        AssertRestoredFilesystem(true);
    }

    [Fact]
    public void Apply_CancellationTransfersAllHistoricalOwnersAndLeavesUnrelatedPackagesUntouched()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([
            Installer("B", 1, ("Shared", "B"), ("BF", "B")),
            Installer("C", 1, ("CF", "C")),
            Installer("Unrelated", 1, ("UF", "U"))
        ]);
        // Historical state may contain multiple owners, including differently cased paths.
        state = new Dictionary<string, PackageInstallationState>(state)
        {
            ["C"] = state["C"] with { Files = ["shared", "CF"] }
        };
        var unrelated = state["Unrelated"];
        using var cancellation = new CancellationTokenSource();
        events.Setup(e => e.ProgressUpdate(It.IsAny<IPercent>())).Callback(cancellation.Cancel);

        Apply([
            Installer("A", 1, ("SHARED", "A")),
            Installer("B", 1, ("Shared", "B"), ("BF", "B")),
            Installer("C", 1, ("Shared", "C"), ("CF", "C")),
            Installer("Unrelated", 1, ("UF", "U"))
        ], cancellation.Token);

        state["B"].Files.Should().Equal("BF");
        state["C"].Files.Should().Equal("CF");
        state["B"].ShadowedBy.Should().Equal("A");
        state["C"].ShadowedBy.Should().Equal("A");
        state["Unrelated"].Should().BeSameAs(unrelated);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("A");
        AssertOriginalBackup(true);

        Apply([
            Installer("B", 1, ("Shared", "B"), ("BF", "B")),
            Installer("C", 1, ("Shared", "C"), ("CF", "C")),
            Installer("Unrelated", 1, ("UF", "U"))
        ]);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("B");
        state["C"].ShadowedBy.Should().Equal("B");

        Apply([]);
        AssertRestoredFilesystem(true);
    }

    [Fact]
    public void Apply_ReordersOneOfMultipleShadowers()
    {
        Apply([
            Installer("A", 1, ("X", "A"), ("AF", "A")),
            Installer("C", 1, ("Y", "C"), ("CF", "C")),
            Installer("B", 1, ("X", "B"), ("Y", "B"), ("BF", "B"))
        ]);
        state["B"].ShadowedBy.Should().BeEquivalentTo(["A", "C"]);

        for (var run = 0; run < 2; run++)
        {
            Apply([
                Installer("A", 1, ("X", "A"), ("AF", "A")),
                Installer("B", 1, ("X", "B"), ("Y", "B"), ("BF", "B")),
                Installer("C", 1, ("Y", "C"), ("CF", "C"))
            ]);
            fs.File.ReadAllText(PathOf("X")).Should().Be("A");
            fs.File.ReadAllText(PathOf("Y")).Should().Be("B");
            state["B"].ShadowedBy.Should().Equal("A");
            state["C"].ShadowedBy.Should().Equal("B");
        }
    }

    [Fact]
    public void Apply_RemovingOneOfMultipleShadowersCanResumeAfterCancellation()
    {
        Apply([
            Installer("A", 1, ("X", "A"), ("AF", "A")),
            Installer("C", 1, ("Y", "C"), ("CF", "C")),
            Installer("B", 1, ("X", "B"), ("Y", "B"), ("BF", "B"))
        ]);
        using var cancellation = new CancellationTokenSource();
        events.Setup(e => e.UninstallingPackage("C")).Callback(cancellation.Cancel);
        Apply([
            Installer("A", 1, ("X", "A"), ("AF", "A")),
            Installer("B", 1, ("X", "B"), ("Y", "B"), ("BF", "B"))
        ], cancellation.Token);
        state.Should().NotContainKey("C");

        Apply([
            Installer("A", 1, ("X", "A"), ("AF", "A")),
            Installer("B", 1, ("X", "B"), ("Y", "B"), ("BF", "B"))
        ]);

        fs.File.ReadAllText(PathOf("Y")).Should().Be("B");
        state["B"].ShadowedBy.Should().Equal("A");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_FailedBackupPreservesOriginalOnCleanupAndRetry(bool removal)
    {
        fs.AddFile(PathOf("Shared"), "Original");
        var fail = true;
        backups.Setup(b => b.PerformBackup(new RootedPath(installDir, "Shared")))
            .Callback<RootedPath>(path =>
            {
                if (fail) throw new IOException("Backup failed");
                realBackups.PerformBackup(path);
            });
        var reconcile = () => Apply([Installer("A", 1, (removal ? "Shared-remove" : "Shared", "A"))]);

        reconcile.Should().Throw<IOException>();
        state.Should().BeEmpty();
        Apply([]);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("Original");
        AssertOriginalBackup(false);

        fail = false;
        reconcile();
        fs.File.Exists(PathOf("Shared")).Should().Be(!removal);
        AssertOriginalBackup(true);
        Apply([]);
        AssertRestoredFilesystem(true);
    }

    [Fact]
    public void Apply_FailedWriteRetainsBackedUpFileForRecovery()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        var files = new Mock<IFile>();
        files.Setup(f => f.WriteAllText(PathOf("Shared"), "A")).Callback(() =>
        {
            fs.File.WriteAllText(PathOf("Shared"), "Partial write");
            fs.File.SetCreationTimeUtc(PathOf("Shared"), time.GetUtcNow().AddMinutes(1).UtcDateTime);
            throw new IOException("Write failed");
        });
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.SetupGet(f => f.File).Returns(files.Object);
        fileSystem.SetupGet(f => f.Directory).Returns(fs.Directory);
        var reconcile = () => Apply([new StaticFilesInstaller(fileSystem.Object, time, "A", 1,
            new Dictionary<string, string> { ["Shared"] = "A" }, [])]);

        reconcile.Should().Throw<IOException>();
        state["A"].Partial.Should().BeTrue();
        state["A"].Files.Should().Equal("Shared");
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("Partial write");
        AssertOriginalBackup(true);

        Apply([]);
        AssertRestoredFilesystem(true);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void Apply_OwnershipTransferPreservesUpdatedGameOriginal(int nextVersion, bool removal)
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("B", 1, ("Shared", "B"), ("BF", "B"))]);
        fs.File.WriteAllText(PathOf("Shared"), "Updated game version");
        fs.File.SetCreationTimeUtc(PathOf("Shared"), time.GetUtcNow().UtcDateTime);
        time.Advance(TimeSpan.FromMinutes(1));

        Apply([
            Installer("A", 1, (removal ? "Shared-remove" : "Shared", "A")),
            Installer("B", nextVersion, ("Shared", "B2"), ("BF", "B2"))
        ]);

        fs.File.ReadAllText(PathOf("Shared.orig")).Should().Be("Updated game version");
        Apply([]);
        state.Should().BeEmpty();
        fs.AllFiles.Should().Equal(PathOf("Shared"));
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("Updated game version");
    }

    [Fact]
    public void Apply_FailedTransferBackupLeavesUpdatedOriginalWithPreviousOwner()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("B", 1, ("Shared", "B"), ("BF", "B"))]);
        fs.File.WriteAllText(PathOf("Shared"), "Updated game version");
        fs.File.SetCreationTimeUtc(PathOf("Shared"), time.GetUtcNow().UtcDateTime);
        time.Advance(TimeSpan.FromMinutes(1));
        backups.Setup(b => b.PerformBackup(new RootedPath(installDir, "Shared"))).Throws<IOException>();
        var reconcile = () => Apply([
            Installer("A", 1, ("Shared", "A")),
            Installer("B", 1, ("Shared", "B"), ("BF", "B"))
        ]);

        reconcile.Should().Throw<IOException>();
        state.Should().NotContainKey("A");
        state["B"].Files.Should().BeEquivalentTo(["Shared", "BF"]);
        state["B"].ShadowedBy.Should().BeEmpty();

        Apply([]);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("Updated game version");
        AssertOriginalBackup(false);
    }

    [Fact]
    public void Apply_ReportsProgressPerPackageIncludingReinstallPairs()
    {
        Apply([Installer("A", 1, ("AF", "A")), Installer("B", 1, ("BF", "B"))]);
        var progress = new List<double>();
        events.Setup(e => e.ProgressUpdate(It.IsAny<IPercent>()))
            .Callback<IPercent>(p => progress.Add(p.Percent));

        Apply([Installer("A", 2, ("AF", "A2")), Installer("B", 1, ("BF", "B"))]);

        progress.Should().Equal(0.5, 1.0);
    }

    [Fact]
    public void Apply_DecisionHookSeesDependenciesFromKeptAndNewlyInstalledPackages()
    {
        var kept = new StaticFilesInstaller(fs, time, "Kept", 1,
            new Dictionary<string, string> { ["KF"] = "K" }, ["ExistingDependency"]);
        Apply([kept]);

        var inner = Installer("New", 1, ("NF", "N"));
        var discovered = new Mock<IPackageInstaller>();
        discovered.SetupGet(i => i.PackageName).Returns(inner.PackageName);
        discovered.SetupGet(i => i.PackageVersionHash).Returns(inner.PackageVersionHash);
        discovered.SetupGet(i => i.InstallTime).Returns(inner.InstallTime);
        discovered.SetupGet(i => i.Installed).Returns(() => inner.Installed);
        discovered.SetupGet(i => i.InstalledFiles).Returns(() => inner.InstalledFiles);
        discovered.SetupGet(i => i.PackageDependencies).Returns(() =>
            inner.Installed == IInstallation.State.Installed
                ? new HashSet<string> { "NewDependency" }
                : new HashSet<string>());
        discovered.Setup(i => i.Install(It.IsAny<IInstaller.Destination>(), It.IsAny<IBackupStrategy>(),
                It.IsAny<ProcessingCallbacks<RootedPath>>()))
            .Callback<IInstaller.Destination, IBackupStrategy, ProcessingCallbacks<RootedPath>>(inner.Install);

        var observed = false;
        service.BeforeDecision = (name, current) =>
        {
            if (name != "Last") return;
            observed = true;
            current["Kept"].Dependencies.Should().Equal("ExistingDependency");
            current["New"].Dependencies.Should().Equal("NewDependency");
        };

        Apply([Installer("Kept", 1, ("KF", "Unused")), discovered.Object, Installer("Last", 1, ("LF", "L"))]);

        observed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_ResumesAfterAnUpdatedShadowerDropsAFile(bool fail)
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([
            Installer("A", 1, ("Shared", "A"), ("AF", "A")),
            Installer("B", 1, ("Shared", "B"), ("BF", "B"))
        ]);
        using var cancellation = new CancellationTokenSource();
        events.Setup(e => e.ProgressUpdate(It.IsAny<IPercent>())).Callback(() =>
        {
            if (fail) throw new IOException("Interrupted after updating A");
            cancellation.Cancel();
        });
        var apply = () => Apply([
            Installer("A", 2, ("AF", "A2")),
            Installer("B", 1, ("Shared", "B"), ("BF", "B"))
        ], cancellation.Token);
        if (fail) apply.Should().Throw<IOException>();
        else apply();
        state["A"].VersionHash.Should().Be(2);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("Original");
        ReloadState();
        events.Reset();

        Apply([
            Installer("A", 2, ("AF", "A2")),
            Installer("B", 1, ("Shared", "B"), ("BF", "B"))
        ]);

        fs.File.ReadAllText(PathOf("Shared")).Should().Be("B");
        state["B"].ShadowedBy.Should().BeEmpty();
        Apply([]);
        AssertRestoredFilesystem(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_RepairsExternallyChangedFilesAndPreservesTheLatestOriginal(bool removed)
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("A", 1, ("Shared", "A"))]);
        if (removed) fs.File.Delete(PathOf("Shared"));
        else
        {
            fs.File.WriteAllText(PathOf("Shared"), "Updated game original");
            fs.File.SetCreationTimeUtc(PathOf("Shared"), time.GetUtcNow().UtcDateTime);
        }

        Apply([Installer("A", 1, ("Shared", "A"))]);

        fs.File.Exists(PathOf("Shared")).Should().BeTrue();
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("A");
        Apply([]);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be(removed ? "Original" : "Updated game original");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_KeepsRemovalMarkersAndReappliesThemWhenTheGameRestoresAFile(bool recreated)
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("A", 1, ("Shared-remove", ""))]);
        ReloadState();
        events.Invocations.Clear();
        if (recreated)
        {
            fs.AddFile(PathOf("Shared"), "Updated game original");
            fs.File.SetCreationTimeUtc(PathOf("Shared"), time.GetUtcNow().UtcDateTime);
        }

        Apply([Installer("A", 1, ("Shared-remove", ""))]);

        fs.File.Exists(PathOf("Shared")).Should().BeFalse();
        events.Verify(e => e.InstallingPackage("A"), recreated ? Times.Once : Times.Never);
        events.Verify(e => e.SkippingPackage("A"), recreated ? Times.Never : Times.Once);
        Apply([]);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be(recreated ? "Updated game original" : "Original");
    }

    [Fact]
    public void Apply_LegacyRemovalMarkersAreReinstalledOnceToLearnRemovalIntent()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("A", 1, ("Shared-remove", ""))]);
        state = new Dictionary<string, PackageInstallationState>(state)
        {
            ["A"] = state["A"] with { RemovedFiles = [], FileStates = new Dictionary<string, InstalledFileState>() }
        };
        events.Invocations.Clear();

        Apply([Installer("A", 1, ("Shared-remove", ""))]);
        state["A"].RemovedFiles.Should().Equal("Shared");
        events.Verify(e => e.InstallingPackage("A"), Times.Once);
        events.Invocations.Clear();

        Apply([Installer("A", 1, ("Shared-remove", ""))]);
        events.Verify(e => e.SkippingPackage("A"), Times.Once);
        Apply([]);
        AssertRestoredFilesystem(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_FailedGeneratedConfigurationWritesRemainOwnedForCleanup(bool originalExists)
    {
        var config = new ModInstallConfig();
        var configPath = Path.Combine(config.GameSupportedModDir, "A_1", config.VehicleListFileName);
        if (originalExists) fs.AddFile(PathOf(configPath), "Existing configuration");
        var files = new Mock<IFile>();
        files.Setup(f => f.Exists(It.IsAny<string>())).Returns((string path) => fs.File.Exists(path));
        files.Setup(f => f.ReadAllText(It.IsAny<string>())).Returns((string path) => fs.File.ReadAllText(path));
        void WriteThenFail(string path, string contents)
        {
            fs.File.WriteAllText(path, contents);
            fs.File.SetCreationTimeUtc(path, time.GetUtcNow().AddMinutes(1).UtcDateTime);
            throw new IOException("Partial configuration write");
        }
        files.Setup(f => f.AppendAllText(PathOf(configPath), It.IsAny<string>())).Callback<string, string>(WriteThenFail);
        files.Setup(f => f.WriteAllText(PathOf(configPath), It.IsAny<string>())).Callback<string, string>(WriteThenFail);
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.SetupGet(f => f.File).Returns(files.Object);
        fileSystem.SetupGet(f => f.Directory).Returns(fs.Directory);
        var mod = new ModInstaller(fileSystem.Object,
            Installer("A", 1, (Path.Combine("vehicles", "Car.crd"), "Car")), "Temp", config, installDir, "Bootfiles");
        var apply = () => Apply([mod]);

        apply.Should().Throw<IOException>();
        state["A"].Partial.Should().BeTrue();
        fs.File.Exists(PathOf(configPath)).Should().BeTrue();

        Apply([]);

        fs.File.Exists(PathOf(configPath)).Should().Be(originalExists);
        if (originalExists) fs.File.ReadAllText(PathOf(configPath)).Should().Be("Existing configuration");
    }

    [Fact]
    public void Apply_GeneratedConfigurationPreservesPreexistingContentAndRestoresItOnUninstall()
    {
        var config = new ModInstallConfig();
        var configPath = Path.Combine(config.GameSupportedModDir, "A_1", config.VehicleListFileName);
        fs.AddFile(PathOf(configPath), "Existing configuration");
        var crd = Path.Combine("vehicles", "Car.crd");
        var mod = new ModInstaller(fs, Installer("A", 1, (crd, "Car")), "Temp", config, installDir, "Bootfiles");

        Apply([mod]);

        fs.File.ReadAllText(PathOf(configPath)).Should().Be("Existing configuration" + crd);
        Apply([]);
        fs.File.Exists(PathOf(configPath)).Should().BeTrue();
        fs.File.ReadAllText(PathOf(configPath)).Should().Be("Existing configuration");
    }

    [Fact]
    public void Apply_GeneratedConfigurationBackupFailureLeavesTheOriginalUntouchedAndCanRetry()
    {
        var config = new ModInstallConfig();
        var configPath = Path.Combine(config.GameSupportedModDir, "A_1", config.VehicleListFileName);
        var crd = Path.Combine("vehicles", "Car.crd");
        fs.AddFile(PathOf(configPath), "Existing configuration");
        backups.Setup(b => b.PerformBackup(new RootedPath(installDir, configPath))).Throws<IOException>();
        IPackageInstaller Mod() => new ModInstaller(fs, Installer("A", 1, (crd, "Car")), "Temp",
            config, installDir, "Bootfiles");
        var apply = () => Apply([Mod()]);

        apply.Should().Throw<IOException>();
        state["A"].Files.Should().NotContain(configPath);
        fs.File.ReadAllText(PathOf(configPath)).Should().Be("Existing configuration");
        Apply([]);
        fs.File.ReadAllText(PathOf(configPath)).Should().Be("Existing configuration");

        backups.Setup(b => b.PerformBackup(new RootedPath(installDir, configPath)))
            .Callback<RootedPath>(realBackups.PerformBackup);
        apply();
        fs.File.ReadAllText(PathOf(configPath)).Should().Be("Existing configuration" + crd);
        Apply([]);
        fs.File.ReadAllText(PathOf(configPath)).Should().Be("Existing configuration");
    }

    [Fact]
    public void Apply_FailedUninstallNotificationMustNotConvertMissingFilesIntoRemovalMarkers()
    {
        Apply([Installer("A", 1, ("Shared", "A"))]);
        fs.File.Delete(PathOf("Shared"));
        events.Setup(e => e.UninstallingPackage("A")).Throws(new IOException("Logger failed"));
        var apply = () => Apply([Installer("A", 1, ("Shared", "A"))]);
        apply.Should().Throw<IOException>();
        events.Reset();
        ReloadState();

        Apply([Installer("A", 1, ("Shared", "A"))]);

        fs.File.Exists(PathOf("Shared")).Should().BeTrue("the failed notification must not change the expected presence of A's file");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_InPlaceGameUpdateSurvivesUninstall(bool reapply)
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("A", 1, ("Shared", "A"))]);
        ReloadState();
        var creation = fs.File.GetCreationTimeUtc(PathOf("Shared"));
        fs.File.WriteAllText(PathOf("Shared"), "Updated game original");
        fs.File.SetCreationTimeUtc(PathOf("Shared"), creation);
        fs.File.SetLastWriteTimeUtc(PathOf("Shared"), time.GetUtcNow().UtcDateTime);

        if (reapply) Apply([Installer("A", 1, ("Shared", "A"))]);
        Apply([]);

        fs.File.ReadAllText(PathOf("Shared")).Should().Be("Updated game original");
    }

    [Fact]
    public void Apply_UnwritableCreationTimesDoNotPreventInstallationOrRecovery()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        var files = new Mock<IFile>();
        files.Setup(f => f.Exists(It.IsAny<string>())).Returns((string path) => fs.File.Exists(path));
        files.Setup(f => f.GetCreationTimeUtc(It.IsAny<string>())).Returns((string path) => fs.File.GetCreationTimeUtc(path));
        files.Setup(f => f.SetCreationTimeUtc(It.IsAny<string>(), It.IsAny<DateTime>()))
            .Throws(new UnauthorizedAccessException("Cannot set file attributes"));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.SetupGet(f => f.File).Returns(files.Object);
        var provider = new Mock<IBackupStrategyProvider<DateTimeOffset, PackageReconciliationService.IEventHandler>>();
        provider.Setup(p => p.BackupStrategy(It.IsAny<DateTimeOffset>(), It.IsAny<PackageReconciliationService.IEventHandler>()))
            .Returns((DateTimeOffset installedAt, PackageReconciliationService.IEventHandler handler) =>
                new SkipUpdatedBackupStrategy(fileSystem.Object, realBackups, installedAt, handler));
        var failingService = new PackageReconciliationService<PackageReconciliationService.IEventHandler>(provider.Object, fs);
        var earlier = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-1));
        var installer = new StaticFilesInstaller(fs, earlier, "A", 1, new Dictionary<string, string> { ["Shared"] = "A" }, []);
        var package = new Mock<IPackage>();
        package.Setup(p => p.CreateInstaller()).Returns(installer);
        var apply = () => failingService.Reconcile(state, [package.Object], installDir,
            current => state = current, events.Object, CancellationToken.None);

        apply.Should().NotThrow();
        files.Verify(f => f.SetCreationTimeUtc(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        state["A"].Partial.Should().BeFalse();
        fs.File.ReadAllText(PathOf("Shared.orig")).Should().Be("Original");
        ReloadState();

        Apply([]);

        fs.File.ReadAllText(PathOf("Shared")).Should().Be("Original", "failure to stamp a managed file must not make it a game update");
    }

    [Fact]
    public void Apply_FailedPostWriteBookkeepingStillPersistsManagedFileMetadata()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        backups.Setup(b => b.AfterInstall(new RootedPath(installDir, "Shared"))).Callback<RootedPath>(path =>
        {
            fs.File.SetCreationTimeUtc(path.Full, time.GetUtcNow().AddMinutes(1).UtcDateTime);
            throw new IOException("Bookkeeping failed after writing");
        });
        var apply = () => Apply([Installer("A", 1, ("Shared", "A"))]);

        apply.Should().Throw<IOException>();
        state["A"].Partial.Should().BeTrue();
        ReloadState();
        Apply([]);

        AssertRestoredFilesystem(true);
    }

    private void ReloadState()
    {
        var savedState = PathOf("state.json");
        var persistence = new JsonFileStatePersistence(fs, savedState, PathOf("installed.json"));
        persistence.WriteState(new SavedState(state));
        state = persistence.ReadState().Installation;
        fs.File.Delete(savedState);
    }

    [Fact]
    public void Apply_HistoricalPathAliasesRestoreOnePhysicalFileOnlyOnce()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("A", 1, ("Shared", "A"))]);
        state = new Dictionary<string, PackageInstallationState>(state)
        {
            ["B"] = state["A"] with { Files = [".\\Shared"] }
        };

        Apply([]);

        AssertRestoredFilesystem(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_FileMetadataDetectsInPlaceUpdatesByLengthOrWriteTime(bool changeLength)
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("A", 1, ("Shared", "A"))]);
        var writeTime = fs.File.GetLastWriteTimeUtc(PathOf("Shared"));
        var update = changeLength ? "Updated game original" : "B";
        fs.File.WriteAllText(PathOf("Shared"), update);
        fs.File.SetLastWriteTimeUtc(PathOf("Shared"), changeLength ? writeTime : writeTime.AddSeconds(1));
        ReloadState();

        Apply([Installer("A", 1, ("Shared", "A"))]);

        fs.File.ReadAllText(PathOf("Shared")).Should().Be("A");
        Apply([]);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be(update);
    }

    [Fact]
    public void Apply_OwnershipTransferPreservesAnInPlaceGameUpdate()
    {
        fs.AddFile(PathOf("Shared"), "Original");
        Apply([Installer("B", 1, ("Shared", "B"), ("BF", "B"))]);
        fs.File.WriteAllText(PathOf("Shared"), "Updated game original");
        ReloadState();

        Apply([Installer("A", 1, ("Shared", "A")), Installer("B", 1, ("Shared", "B"), ("BF", "B"))]);

        fs.File.ReadAllText(PathOf("Shared.orig")).Should().Be("Updated game original");
        Apply([]);
        fs.File.ReadAllText(PathOf("Shared")).Should().Be("Updated game original");
    }

    private IPackageInstaller Installer(string name, int version, params (string Path, string Contents)[] files) =>
        new StaticFilesInstaller(fs, time, name, version,
            files.ToDictionary(f => f.Path, f => f.Contents), []);

    private void Apply(IPackageInstaller[] installers, CancellationToken cancellation = default)
    {
        var packages = installers.Select(installer =>
        {
            var package = new Mock<IPackage>();
            package.Setup(p => p.CreateInstaller()).Returns(installer);
            return package.Object;
        }).ToArray();
        service.Reconcile(state, packages, installDir, updated => state = updated, events.Object, cancellation);
        time.Advance(TimeSpan.FromMinutes(1));
    }

    private string PathOf(string relative) => Path.Combine(installDir, relative);

    private void AssertOriginalBackup(bool exists)
    {
        fs.File.Exists(PathOf("Shared.orig")).Should().Be(exists);
        if (exists)
        {
            fs.File.ReadAllText(PathOf("Shared.orig")).Should().Be("Original");
        }
    }

    private void AssertRestoredFilesystem(bool originalExists)
    {
        fs.AllFiles.Select(path => (path.ToUpperInvariant(), fs.File.ReadAllText(path))).Should().BeEquivalentTo(
            originalExists ? new[] { (PathOf("Shared").ToUpperInvariant(), "Original") } : []);
    }

    private class BackupNaming : MoveFileBackupStrategy.IBackupFileNaming
    {
        public string ToBackup(string fullPath) => fullPath + ".orig";
        public bool IsBackup(string fullPath) => fullPath.EndsWith(".orig");
    }

    private class ObservingService(
        IBackupStrategyProvider<DateTimeOffset, PackageReconciliationService.IEventHandler> provider,
        IFileSystem fileSystem)
        : PackageReconciliationService<PackageReconciliationService.IEventHandler>(provider, fileSystem)
    {
        internal Action<string, IReadOnlyDictionary<string, PackageInstallationState>>? BeforeDecision { get; set; }

        protected override IReadOnlyCollection<IReconciliationAction> ReconciliationActions(
            string packageName, IPackageInstaller? installer, ReconciliationContext context)
        {
            BeforeDecision?.Invoke(packageName, context.CurrentState);
            return base.ReconciliationActions(packageName, installer, context);
        }
    }
}
