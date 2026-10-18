using Core.Mods.Installation.Installers;
using FluentAssertions;

namespace Core.Tests.Mods.Installation.Installers;

[UnitTest]
public class DrivelineRecordsTest
{
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", false)]
    [InlineData("\r\n", true)]
    public void Parse_PreservesCompleteRecordsAndRelativeIndentation(string newline, bool trailingBlankLine)
    {
        var contents = string.Join(newline,
            "Readme introduction", "", "    RECORD First", "        Ratio 1", "", "",
            "    RECORD Second", "        Ratio 1", "        SecondOnly 2");
        if (trailingBlankLine) contents += newline + newline;

        var records = DrivelineRecords.Parse(contents);

        records.Should().Equal(
            $"RECORD First{Environment.NewLine}    Ratio 1",
            $"RECORD Second{Environment.NewLine}    Ratio 1{Environment.NewLine}    SecondOnly 2");
    }

    [Fact]
    public void Parse_LessIndentedBodyLinesRetainTheirText()
    {
        var contents = "    RECORD First\n        Ratio 1\n  X\nY\n\n    RECORDGROUP Second\n        Ratio 2";

        DrivelineRecords.Parse(contents).Should().Equal(
            $"RECORD First{Environment.NewLine}    Ratio 1{Environment.NewLine}X{Environment.NewLine}Y",
            $"RECORDGROUP Second{Environment.NewLine}    Ratio 2");
    }
}
