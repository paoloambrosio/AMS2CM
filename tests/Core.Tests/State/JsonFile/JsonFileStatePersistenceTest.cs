using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Core.Packages.Installation;
using Core.State;
using Core.State.JsonFile;
using FluentAssertions;

namespace Core.Tests.State.JsonFile;

[IntegrationTest]
public class JsonFileStatePersistenceTest
{
    private const string StateV2File = "v2";
    private const string StateV1File = "v1";

    [Fact]
    public void ReadState_EmptyStateWhenNoStateFileExists()
    {
        var fs = new MockFileSystem();
        var sp = new JsonFileStatePersistence(fs, StateV2File, StateV1File);

        sp.ReadState().Should().Be(SavedState.Empty());
    }

    [Fact]
    public void ReadState_V2()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { StateV2File, new MockFileData(
                    """
                    {
                        "Install": {
                            "Mods": {
                                "M": {
                                    "Time": "2007-06-05T11:22:33+04",
                                    "VersionHash": 101,
                                    "Dependencies": ["D"],
                                    "Files": ["F"],
                                    "ShadowedBy": ["S"],
                                }
                            }
                        }
                    }
                    """)
            }
        });
        var sp = new JsonFileStatePersistence(fs, fs.Path.GetFullPath(StateV2File), "NotUsed");

        var state = sp.ReadState();
        state.Installation.Keys.Should().Contain("M");

        var mod = state.Installation["M"];
        mod.Time.Should().Be(DateTimeOffset.Parse("2007-06-05T11:22:33+04").ToUniversalTime());
        mod.VersionHash.Should().Be(101);
        mod.Dependencies.Should().Contain("D");
        mod.Files.Should().Contain("F");
        mod.ShadowedBy.Should().Contain("S");
        mod.RemovedFiles.Should().BeEmpty();
        mod.FileStates.Should().BeEmpty();
        mod.InstallationFormatVersion.Should().Be(0);
    }

    [Fact]
    public void ReadState_V2DefaultValues()
    {
        var fileWriteTime = DateTimeOffset.Now.AddDays(-1);
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { StateV2File, new MockFileData(
                """
                {
                    "Install": {
                        "Mods": {
                            "M": {
                                "FsHash": 102
                            }
                        }
                    }
                }
                """) { LastWriteTime = fileWriteTime }
            }
        });
        var sp = new JsonFileStatePersistence(fs, fs.Path.GetFullPath(StateV2File), "NotUsed");

        var state = sp.ReadState();
        state.Installation.Keys.Should().Contain("M");

        var mod = state.Installation["M"];
        mod.Time.Should().Be(fileWriteTime.ToUniversalTime());
        // FsHash not recognised to force reinstall from 0.3
        mod.VersionHash.Should().BeNull();
        mod.Dependencies.Should().BeEmpty();
        mod.Files.Should().BeEmpty();
        mod.ShadowedBy.Should().BeEmpty();
    }

    [Fact]
    public void ReadState_V2ModsInstallTimeDefaultsToGlobal()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { StateV2File,
                """
                {
                    "Install": {
                        "Time": "1970-01-01T00:00:00Z",
                        "Mods": {
                            "M": {
                            }
                        }
                    }
                }
                """
            }
        });
        var sp = new JsonFileStatePersistence(fs, fs.Path.GetFullPath(StateV2File), "NotUsed");

        var state = sp.ReadState();
        state.Installation.Keys.Should().Contain("M");

        var mod = state.Installation["M"];
        mod.Time.Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void ReadState_V1()
    {
        var fileWriteTime = DateTimeOffset.Now.AddDays(-1);
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { StateV1File, new MockFileData(
                """
                {
                    "M": ["F"]
                }
                """) { LastWriteTime = fileWriteTime }
            }
        });
        var sp = new JsonFileStatePersistence(fs, "NotUsed", fs.Path.GetFullPath(StateV1File));

        var state = sp.ReadState();
        state.Installation.Keys.Should().Contain("M");

        var mod = state.Installation["M"];
        mod.Time.Should().Be(fileWriteTime.ToUniversalTime());
        mod.VersionHash.Should().Be(JsonFileStatePersistence.UnkownVersionHash);
        mod.Dependencies.Should().BeEmpty();
        mod.Files.Should().Contain("F");
        mod.ShadowedBy.Should().BeEmpty();
    }

    [Fact]
    public void ReadState_FavoursLatestStateFile()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { StateV2File,
                """
                {
                    "Install": {
                        "Mods": {
                            "V2": {
                            }
                        }
                    }
                }
                """
            },
            { StateV1File,
                """
                {
                    "V1": []
                }
                """
            }
        });
        var sp = new JsonFileStatePersistence(fs, fs.Path.GetFullPath(StateV2File), fs.Path.GetFullPath(StateV1File));

        var state = sp.ReadState();

        state.Installation.Keys.Should().Contain("V2");
    }

    [Theory]
    [InlineData(StateV1File)]
    [InlineData(StateV2File)]
    public void ReadState_ReadFailure(string stateFile)
    {
        var filesystemFailureMessage = "Simulated read failure";
        var fs = new Mock<IFileSystem>();
        fs.Setup(x => x.File.Exists(stateFile)).Returns(true);
        fs.Setup(x => x.File.ReadAllText(stateFile))
            .Throws(new IOException(filesystemFailureMessage));

        var sp = new JsonFileStatePersistence(fs.Object, StateV2File, StateV1File);

        Action read = () => sp.ReadState();
        read.Should().Throw<Exception>()
            .WithoutMessage(filesystemFailureMessage)
            .WithInnerException<IOException>();
    }

    [Theory]
    [InlineData(StateV1File)]
    [InlineData(StateV2File)]
    public void ReadState_DecodingFailure(string stateFile)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { stateFile, """{"not terminated""" }
        });

        var sp = new JsonFileStatePersistence(fs, StateV2File, StateV1File);

        Action read = () => sp.ReadState();
        read.Should().Throw<Exception>()
            .WithInnerException<Newtonsoft.Json.JsonReaderException>();
    }

    [Fact]
    public void WriteState_V2WritesUtc()
    {
        var fs = new MockFileSystem();
        var sp = new JsonFileStatePersistence(fs, fs.Path.GetFullPath(StateV2File), "NotUsed");

        var localTime = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(4));

        sp.WriteState(new SavedState(
            Installation: new Dictionary<string, PackageInstallationState>
            {
                ["P"] = new(
                    Time: localTime,
                    VersionHash: null,
                    Partial: false,
                    Dependencies: [],
                    Files: [],
                    ShadowedBy: []),
            }
        ));

        fs.GetFile(StateV2File).TextContents.Should().Contain(""""Time":"1970-01-01T00:00:00Z"""");
    }

    [Fact]
    public void WriteState_DeletesPreviousStates()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { StateV1File, "NotUsed" }
        });
        var sp = new JsonFileStatePersistence(fs, fs.Path.GetFullPath(StateV2File), fs.Path.GetFullPath(StateV1File));

        sp.WriteState(SavedState.Empty());

        fs.AllFiles.Should().BeEquivalentTo(fs.Path.GetFullPath(StateV2File));
    }

    [Fact]
    public void WriteState_WriteFailure()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { Path.Combine(StateV2File, "File"), "NotUsed" }
        });
        var sp = new JsonFileStatePersistence(fs, fs.Path.GetFullPath(StateV2File), fs.Path.GetFullPath(StateV1File));

        Action write = () => sp.WriteState(SavedState.Empty());
        write.Should().Throw<Exception>()
            .WithInnerException<IOException>();
    }

    [Fact]
    public void WriteReadLoop()
    {
        var fs = new MockFileSystem();
        var sp = new JsonFileStatePersistence(fs, fs.Path.GetFullPath(StateV2File), "NotUsed");

        var writtenState = new SavedState(
            Installation: new Dictionary<string, PackageInstallationState>
            {
                ["P"] = new(
                    Time: DateTimeOffset.UtcNow, VersionHash: 42, Partial: true,
                    Dependencies: ["D"],
                    Files: ["F", "Removed", "Fingerprint", "Unknown"],
                    ShadowedBy: ["S"])
                {
                    InstallationFormatVersion = 1,
                    RemovedFiles = ["Removed"],
                    FileStates = new Dictionary<string, InstalledFileState>
                    {
                        ["F"] = new(true, 17, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow),
                        ["Removed"] = new(false, 0, default, default),
                        ["Fingerprint"] = new(true, 0, default, default)
                        {
                            MetadataUnavailable = true,
                            ContentHash = "ABCDEF"
                        },
                        ["Unknown"] = InstalledFileState.Unknown
                    }
                },
            }
        );

        sp.WriteState(writtenState);
        var readState = sp.ReadState();

        readState.Should().BeEquivalentTo(writtenState);
    }

    [Theory]
    [InlineData(StateV1File, "")]
    [InlineData(StateV1File, "null")]
    [InlineData(StateV1File, "{\"M\":null}")]
    [InlineData(StateV2File, "")]
    [InlineData(StateV2File, "null")]
    [InlineData(StateV2File, "{}")]
    [InlineData(StateV2File, "{\"Install\":null}")]
    [InlineData(StateV2File, "{\"Install\":{}}")]
    [InlineData(StateV2File, "{\"Install\":{\"Mods\":null}}")]
    [InlineData(StateV2File, "{\"Install\":{\"Mods\":{\"M\":null}}}")]
    public void ReadState_RejectsInvalidExistingState(string path, string contents)
    {
        var fs = new MockFileSystem();
        fs.AddFile(path, contents);
        var persistence = new JsonFileStatePersistence(fs, StateV2File, StateV1File);

        persistence.Invoking(p => p.ReadState()).Should().Throw<InvalidDataException>();
        fs.File.ReadAllText(path).Should().Be(contents);
    }

    [Theory]
    [InlineData(false, "create")]
    [InlineData(false, "write")]
    [InlineData(false, "flush")]
    [InlineData(false, "promote")]
    [InlineData(true, "create")]
    [InlineData(true, "write")]
    [InlineData(true, "flush")]
    [InlineData(true, "promote")]
    public void WriteState_FailuresPreserveThePreviousReadableSnapshot(bool existingV2, string failure)
    {
        var fs = new MockFileSystem();
        fs.AddFile(StateV1File, "{\"Legacy\":[\"Original\"]}");
        if (existingV2) fs.AddFile(StateV2File, "{\"Install\":{\"Mods\":{\"Current\":{\"Files\":[\"Original\"]}}}}");
        var previousFiles = fs.AllFiles.ToDictionary(p => p, p => fs.File.ReadAllText(p));
        var previous = new JsonFileStatePersistence(fs, StateV2File, StateV1File).ReadState();
        var file = new Mock<IFile>();
        file.Setup(f => f.Exists(It.IsAny<string>())).Returns((string p) => fs.File.Exists(p));
        file.Setup(f => f.Delete(It.IsAny<string>())).Callback<string>(fs.File.Delete);
        file.Setup(f => f.Replace(It.IsAny<string>(), StateV2File, null)).Throws(new IOException("promote"));
        file.Setup(f => f.Move(It.IsAny<string>(), StateV2File)).Throws(new IOException("promote"));
        var streams = new Mock<IFileStreamFactory>();
        streams.Setup(f => f.New(It.IsAny<string>(), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            .Returns((string path, FileMode _, FileAccess _, FileShare _) =>
            {
                if (failure == "create") throw new IOException("create");
                if (failure == "promote") return fs.FileStream.New(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var stream = new Mock<FileSystemStream>(new MemoryStream(), path, false) { CallBase = true };
                if (failure == "write")
                    stream.Setup(s => s.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                        .Throws(new IOException("write"));
                else stream.Setup(s => s.Flush(true)).Throws(new IOException("flush"));
                return stream.Object;
            });
        var failingFs = new Mock<IFileSystem>();
        failingFs.SetupGet(f => f.File).Returns(file.Object);
        failingFs.SetupGet(f => f.FileStream).Returns(streams.Object);
        var persistence = new JsonFileStatePersistence(failingFs.Object, StateV2File, StateV1File);

        persistence.Invoking(p => p.WriteState(SavedState.Empty())).Should().Throw<Exception>()
            .WithInnerException<IOException>().WithMessage(failure);

        fs.AllFiles.ToDictionary(p => p, p => fs.File.ReadAllText(p)).Should().BeEquivalentTo(previousFiles);
        new JsonFileStatePersistence(fs, StateV2File, StateV1File).ReadState().Should().BeEquivalentTo(previous);
    }

    [Fact]
    public void WriteState_ObsoleteStateCleanupFailureDoesNotInvalidateCommit()
    {
        var fs = new MockFileSystem();
        fs.AddFile(StateV1File, "{\"Legacy\":[\"Original\"]}");
        var file = new Mock<IFile>();
        file.Setup(f => f.Exists(It.IsAny<string>())).Returns((string p) => fs.File.Exists(p));
        file.Setup(f => f.Move(It.IsAny<string>(), StateV2File)).Callback<string, string>(fs.File.Move);
        file.Setup(f => f.Delete(It.IsAny<string>())).Callback<string>(fs.File.Delete);
        file.Setup(f => f.Delete(StateV1File)).Throws<UnauthorizedAccessException>();
        var wrapped = new Mock<IFileSystem>();
        wrapped.SetupGet(f => f.File).Returns(file.Object);
        wrapped.SetupGet(f => f.FileStream).Returns(fs.FileStream);

        new JsonFileStatePersistence(wrapped.Object, StateV2File, StateV1File).WriteState(SavedState.Empty());

        fs.File.Exists(StateV1File).Should().BeTrue();
        new JsonFileStatePersistence(fs, StateV2File, StateV1File).ReadState().Installation.Should().BeEmpty();
    }

    [Fact]
    public void WriteState_AtomicallyReplacesARealStateFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var persistence = new JsonFileStatePersistence(directory);
            File.WriteAllText(Path.Combine(directory, "installed.json"), "{\"Legacy\":[\"Original\"]}");
            persistence.WriteState(persistence.ReadState());
            persistence.ReadState().Installation.Should().ContainKey("Legacy");
            persistence.WriteState(SavedState.Empty());
            persistence.ReadState().Installation.Should().BeEmpty();
            Directory.GetFiles(directory).Select(Path.GetFileName).Should().Equal("state.json");
        }
        finally { Directory.Delete(directory, true); }
    }
}
