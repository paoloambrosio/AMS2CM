using System.IO.Abstractions;
using Core.Mods.Installation.Installers;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Tests.Base;
using Core.Utils;
using FluentAssertions;
using static Core.Mods.Installation.Installers.GeneratedBootfilesInstaller;

namespace Core.Tests.Mods.Installation.Installers;

[IntegrationTest]
public class GeneratedBootfilesInstallationTest : AbstractFilesystemTest
{
    [Fact]
    public void InstallationUsesTheInjectedFilesystemForGeneratedFilesAndRemovalMarkers()
    {
        var real = new FileSystem();
        var files = new Mock<IFile>();
        files.Setup(f => f.Exists(It.IsAny<string>())).Returns(real.File.Exists);
        files.Setup(f => f.Create(It.IsAny<string>())).Returns((string path) => real.File.Create(path));
        files.Setup(f => f.Copy(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>(real.File.Copy);
        files.Setup(f => f.Move(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<string, string, bool>(real.File.Move);
        files.Setup(f => f.Delete(It.IsAny<string>())).Callback<string>(real.File.Delete);
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.SetupGet(f => f.File).Returns(files.Object);
        fileSystem.SetupGet(f => f.Directory).Returns(real.Directory);
        fileSystem.SetupGet(f => f.FileInfo).Returns(real.FileInfo);
        fileSystem.SetupGet(f => f.Path).Returns(real.Path);
        var game = new RootedPath(TestDir.CreateSubdirectory("Game").FullName);
        var temp = Path.Combine(TestDir.FullName, "Temp");
        foreach (var name in new[] { BootFlowPakFileName, BootSplashPakFileName, PhysicsPersistentPakFileName })
        {
            CreateFile(game.SubPath(Path.Combine(PakfilesDirectory, name)), name);
        }

        var extracted = new List<string>();
        var installer = new GeneratedBootfilesInstaller(fileSystem.Object, "Generated", game.Full, temp,
            (source, destination) =>
            {
                var name = Path.GetFileName(source);
                extracted.Add(name);
                var root = new RootedPath(destination);
                if (name == BootFlowPakFileName)
                {
                    CreateFile(root.SubPath(Path.Combine("Vehicles", "Car", "Car.crd")), "Car");
                }
                else
                {
                    var effects = root.SubPath(Path.Combine("vehicles", "_data", "effects", "backfire"));
                    CreateFile(effects.SubPath("Effect.bmt"), "Excluded");
                    CreateFile(effects.SubPath("Effect.txt"), "Kept");
                }
            });
        var version = installer.PackageVersionHash;

        installer.Install(game.SubPath, Mock.Of<IBackupStrategy>(), new ProcessingCallbacks<RootedPath>());

        extracted.Should().Equal(BootFlowPakFileName, PhysicsPersistentPakFileName);
        installer.Installed.Should().Be(IInstallation.State.Installed);
        installer.PackageVersionHash.Should().Be(version);
        File.ReadAllText(game.SubPath(Path.Combine("Vehicles", "Car", "Car.crd")).Full).Should().Be("Car");
        File.ReadAllText(game.SubPath(Path.Combine(PakfilesDirectory, BootFlowPakFileName)).Full)
            .Should().Be(BootSplashPakFileName);
        var physicsPath = game.SubPath(Path.Combine(PakfilesDirectory, PhysicsPersistentPakFileName));
        File.Exists(physicsPath.Full).Should().BeFalse();
        installer.InstalledFiles.Should().Contain(physicsPath);
        var effectsPath = game.SubPath(Path.Combine("vehicles", "_data", "effects", "backfire"));
        File.Exists(effectsPath.SubPath("Effect.bmt").Full).Should().BeFalse();
        File.ReadAllText(effectsPath.SubPath("Effect.txt").Full).Should().Be("Kept");
        Directory.GetFiles(temp, "*", SearchOption.AllDirectories).Should().ContainSingle()
            .Which.Should().EndWith(PhysicsPersistentPakFileName + BaseInstaller.RemoveFileSuffix);
        files.Verify(f => f.Move(It.IsAny<string>(), It.IsAny<string>(), true), Times.Exactly(3));
        files.Verify(f => f.Delete(physicsPath.Full), Times.Once);
    }
}
