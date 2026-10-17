using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Core.Utils;
using FluentAssertions;

namespace Core.Tests.Utils;

[UnitTest]
public class FileSystemHashTest
{
    private readonly MockFileSystem fs = new();
    private static readonly DateTime TestWriteTime = DateTime.Today;

    [Fact]
    public void Hash32_DifferentContentLength_ChangesHash()
    {
        var hash1 = FileSystemHash.Hash32(AddFile("1", "Content1", TestWriteTime));
        var hash2 = FileSystemHash.Hash32(AddFile("2", "Content2", TestWriteTime));
        var hash3 = FileSystemHash.Hash32(AddFile("3", "Shorter", TestWriteTime));

        hash1.Should().Be(hash2);
        hash1.Should().NotBe(hash3);
    }

    [Fact]
    public void Hash32_DifferentWriteTime_ChangesHash()
    {
        var hash1 = FileSystemHash.Hash32(AddFile("1", "Content", TestWriteTime));
        var hash2 = FileSystemHash.Hash32(AddFile("2", "Content", TestWriteTime.AddMinutes(1)));

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Hash32_SingleFileRemainsCompatible()
    {
        var file = AddFile("Single", "Content", TestWriteTime);
        var hasher = new System.IO.Hashing.XxHash3();
        hasher.Append(BitConverter.GetBytes(file.LastWriteTimeUtc.Ticks));
        hasher.Append(BitConverter.GetBytes(file.Length));
        var expected = hasher.GetCurrentHashAsUInt64().GetHashCode();

        FileSystemHash.Hash32(file).Should().Be(expected);
        FileSystemHash.Hash32(new[] { file }).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Hash32_DetectsChangesToEachInput(int index)
    {
        var files = Enumerable.Range(0, 3).Select(i => AddFile(i.ToString(), "Content", TestWriteTime)).ToArray();
        var original = FileSystemHash.Hash32(files);
        FileSystemHash.Hash32(files).Should().Be(original);
        files[index].LastWriteTimeUtc = files[index].LastWriteTimeUtc.AddMinutes(1);
        FileSystemHash.Hash32(files).Should().NotBe(original);
    }

    [Fact]
    public void Hash32_UsesInputOrderButNotPaths()
    {
        var first = AddFile("First", "One", TestWriteTime);
        var second = AddFile("Second", "TwoTwo", TestWriteTime);
        var sameMetadata = AddFile("First.orig", "XYZ", TestWriteTime);

        FileSystemHash.Hash32(new[] { first, second }).Should().Be(FileSystemHash.Hash32(new[] { sameMetadata, second }));
        FileSystemHash.Hash32(new[] { first, second }).Should().NotBe(FileSystemHash.Hash32(new[] { second, first }));
    }

    private IFileInfo AddFile(string path, string contents, DateTime lastWriteTime)
    {
        fs.AddFile(path, new MockFileData(contents) { LastWriteTime = lastWriteTime });
        return fs.FileInfo.New(path);
    }
}
