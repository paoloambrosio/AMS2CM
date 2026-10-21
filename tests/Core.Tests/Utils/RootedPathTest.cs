using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Core.Utils;
using FluentAssertions;

namespace Core.Tests.Utils;

[UnitTest]
public class RootedPathTest
{
    private readonly MockFileSystem fs = new();

    private IDirectoryInfo Root => fs.DirectoryInfo.New(@"C:\root");

    [Fact]
    public void Equals_ComparesFullPathIgnoringCase()
    {
        var path = new RootedPath(Root, @"dir\file.txt");

        path.Equals(new RootedPath(fs.DirectoryInfo.New(@"C:\root\dir"), "file.txt")).Should().BeTrue();
        path.Equals(new RootedPath(fs.DirectoryInfo.New(@"C:\ROOT"), @"DIR\FILE.TXT")).Should().BeTrue();
        path.Equals(new RootedPath(Root, @"dir\other.txt")).Should().BeFalse();
        path.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_EqualPaths_ReturnsSameHashCode()
    {
        var path = new RootedPath(Root, @"dir\file.txt");
        var anotherFileSystem = new MockFileSystem();
        var equivalent = new RootedPath(anotherFileSystem.DirectoryInfo.New(@"C:\ROOT\DIR"), "FILE.TXT");

        path.Equals(equivalent).Should().BeTrue();
        path.GetHashCode().Should().Be(equivalent.GetHashCode());
        new HashSet<RootedPath> { path, equivalent }.Should().ContainSingle();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(".", "")]
    [InlineData(@".\", "")]
    [InlineData("a//./b", @"a\b")]
    [InlineData(@"a\\b\", @"a\b")]
    [InlineData("version..txt", "version..txt")]
    [InlineData("directory/file.txt", @"directory\file.txt")]
    public void Constructor_NormalizesContainedPaths(string relative, string expected)
    {
        var existingPaths = fs.AllPaths.ToArray();
        var path = new RootedPath(Root, relative);

        path.Root.Should().Be(Root.FullName);
        path.Relative.Should().Be(expected);
        path.Full.Should().Be(fs.Path.Combine(Root.FullName, expected));
        fs.AllPaths.Should().BeEquivalentTo(existingPaths);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"\\server\share\")]
    public void Constructor_SupportsVolumeRoots(string rootName)
    {
        var root = fs.DirectoryInfo.New(rootName);
        var path = new RootedPath(root, @"directory\file.txt");

        path.Full.Should().Be(fs.Path.Combine(root.FullName, @"directory\file.txt"));
        path.Relative.Should().Be(@"directory\file.txt");
    }

    [Theory]
    [InlineData("..")]
    [InlineData(@"..\file")]
    [InlineData("../file")]
    [InlineData(@"a\..\b")]
    [InlineData("a/../b")]
    [InlineData(@"..\root-other\file")]
    [InlineData(@"C:\outside\file")]
    [InlineData("C:file")]
    [InlineData(@"C:..\file")]
    [InlineData(@"\outside\file")]
    [InlineData("/outside/file")]
    [InlineData(@"\\server\share\file")]
    [InlineData(@"\\?\C:\outside\file")]
    [InlineData(@"\\.\device")]
    [InlineData("file:stream")]
    [InlineData(@"dir\file:stream")]
    [InlineData(@"dir\.. \file")]
    [InlineData(@"dir\...\file")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("dir/*")]
    [InlineData("dir/\0file")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData(@"dir\COM1.txt")]
    [InlineData("LPT¹")]
    public void InvalidRelativePathsAreRejectedByAllConstructionMethods(string relative)
    {
        var existingPaths = fs.AllPaths.ToArray();
        var parent = new RootedPath(Root, "parent");

        Action construct = () => new RootedPath(Root, relative);
        Action append = () => parent.SubPath(relative);
        Action replace = () => parent.WithRelativePath(relative);

        construct.Should().Throw<ArgumentException>();
        append.Should().Throw<ArgumentException>();
        replace.Should().Throw<ArgumentException>();
        fs.AllPaths.Should().BeEquivalentTo(existingPaths);
    }

    [Fact]
    public void SubPath_PreservesRootAndAppendsRelativePath()
    {
        var path = new RootedPath(Root, "parent").SubPath("./child//file.txt");

        path.Root.Should().Be(Root.FullName);
        path.Relative.Should().Be(@"parent\child\file.txt");
        path.Full.Should().Be(fs.Path.Combine(Root.FullName, @"parent\child\file.txt"));
    }

    [Fact]
    public void WithRelativePath_PreservesRootAndReplacesRelativePath()
    {
        var path = new RootedPath(Root, @"old\file").WithRelativePath("new/./file");

        path.Root.Should().Be(Root.FullName);
        path.Relative.Should().Be(@"new\file");
    }

    [Fact]
    public void RootIsASnapshotOfTheSuppliedDirectory()
    {
        var root = fs.Directory.CreateDirectory(Root.FullName);
        var path = new RootedPath(root, "child");
        root.MoveTo(@"C:\moved");

        path.Root.Should().Be(@"C:\root");
        path.Full.Should().Be(@"C:\root\child");
        path.SubPath("file").Full.Should().Be(@"C:\root\child\file");
    }
}
