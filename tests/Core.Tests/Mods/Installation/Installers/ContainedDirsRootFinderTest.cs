using System.IO.Abstractions.TestingHelpers;
using System.IO.Abstractions;
using Core.Mods.Installation.Installers;
using FluentAssertions;

namespace Core.Tests.Mods.Installation.Installers;

[UnitTest]
public class ContainedDirsRootFinderTest
{
    private static readonly IPath Paths = new MockFileSystem().Path;
    private static readonly string[] RootDirs = ["R1", "R2"];
    private readonly ContainedDirsRootFinder rootFinder = new(Paths, RootDirs);

    [Fact]
    public void FromDirectoryList_FindsDirsAtRoot()
    {
        rootFinder.FromDirectoryList(
        [
            Paths.Combine("R1"),
        ]).Roots.Should().BeEquivalentTo([
            "",
        ]);
    }

    [Fact]
    public void FromDirectoryList_FindsDirsWhenNamePrefixOfOtherDirs()
    {
        rootFinder.FromDirectoryList(
        [
            Paths.Combine("D1", "R1"),
            Paths.Combine("D11", "R1")
        ]).Roots.Should().BeEquivalentTo([
            "D1",
            "D11"
        ]);
    }

    [Fact]
    public void FromDirectoryList_IgnoresNestedRoots()
    {
        rootFinder.FromDirectoryList(
        [
            Paths.Combine("D1", "D2", "R1"),
            Paths.Combine("D1", "R1"),
            Paths.Combine("D1", "D3", "R1")
        ]).Roots.Should().BeEquivalentTo([
            "D1"
        ]);

        // Empty path is a special case
        rootFinder.FromDirectoryList(
        [
            Paths.Combine("D1", "R1"),
            Paths.Combine("R1"),
            Paths.Combine("D2", "R2")
        ]).Roots.Should().BeEquivalentTo([
            ""
        ]);
    }

    [Fact]
    public void FromDirectoryList_IsCaseInsensitive()
    {
        rootFinder.FromDirectoryList(
        [
            Paths.Combine("d1", "r1"),
        ]).Roots.Should().BeEquivalentTo([
            "d1",
        ]);
    }

    [Fact]
    public void FromDirectoryList_FindsDirsInSubDirs()
    {
        rootFinder.FromDirectoryList(
        [
            Paths.Combine("D1", "R1"),
            Paths.Combine("D2", "D3", "R2"),
            Paths.Combine("D4")
        ]).Roots.Should().BeEquivalentTo([
            "D1",
            Paths.Combine("D2", "D3")
        ]);
    }
}
