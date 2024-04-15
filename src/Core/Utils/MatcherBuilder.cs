using Microsoft.Extensions.FileSystemGlobbing;

namespace Core.Utils;

internal class MatcherBuilder
{
    internal static Matcher Excluding(IEnumerable<string> exclusions)
    {
        var matcher = new Matcher();
        matcher.AddInclude(@"**\*");
        matcher.AddExcludePatterns(exclusions);
        return matcher;
    }
}
