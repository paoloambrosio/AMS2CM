using Core.Utils;
using Microsoft.Extensions.FileSystemGlobbing;
using static Core.Mods.IInstaller;

namespace Core.Mods;

internal abstract class ModInstaller : IInstaller
{
    public interface IConfig
    {
        IEnumerable<string> DirsAtRoot { get; }
        IEnumerable<string> ExcludedFromInstall { get; }
        IEnumerable<string> ExcludedFromConfig { get; }
    }

    private static readonly string GameSupportedModDirectory = Path.Combine("UserData", "Mods");

    private readonly List<string> dirsAtRootLowerCase;
    private readonly Matcher filesToInstallMatcher;
    private readonly Matcher filesToConfigureMatcher;

    protected ModInstaller(IConfig config)
    {
        dirsAtRootLowerCase = config.DirsAtRoot.Select(dir => dir.ToLowerInvariant()).ToList();
        filesToInstallMatcher = MatcherBuilder.Excluding(config.ExcludedFromInstall);
        filesToConfigureMatcher = MatcherBuilder.Excluding(config.ExcludedFromConfig);
    }

    public abstract ConfigEntries Install(string dstPath, ICallbacks<string> fileCallbacks);

    protected ConfigEntries GenerateConfig()
    {
        var gameSupportedMod = FileEntriesToConfigure()
            .Where(p => p.StartsWith(GameSupportedModDirectory))
            .Any();
        return gameSupportedMod
            ? ConfigEntries.Empty
            : new(CrdFileEntries(), TrdFileEntries(), FindDrivelineRecords());
    }

    private List<string> CrdFileEntries() =>
        FileEntriesToConfigure()
            .Where(p => p.EndsWith(".crd"))
            .ToList();

    private List<string> TrdFileEntries() =>
        FileEntriesToConfigure()
            .Where(p => p.EndsWith(".trd"))
            .Select(fp => $"{Path.GetDirectoryName(fp)}{Path.DirectorySeparatorChar}@{Path.GetFileName(fp)}")
            .ToList();

    private IEnumerable<string> FileEntriesToConfigure() =>
        installedFiles.Where(_ => filesToConfigureMatcher.Match(_).HasMatches);

    protected List<string> FindDrivelineRecords(Stream stream)
    {
        var recordBlocks = new List<string>();

        var recordIndent = -1;
        var recordLines = new List<string>();

        string? line;
        using var reader = new StreamReader(stream);
        while ((line = reader.ReadLine()) is not null)
        {
            if (recordIndent < 0)
            {
                recordIndent = line.IndexOf("RECORD", StringComparison.InvariantCulture);
            }

            if (recordIndent < 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                recordIndent = -1;
                recordBlocks.Add(string.Join(Environment.NewLine, recordLines));
                recordLines.Clear();
                continue;
            }
            var lineNoIndent = line.Substring(recordIndent).TrimEnd();
            recordLines.Add(lineNoIndent);
        }

        if (recordIndent >= 0)
        {
            recordBlocks.Add(string.Join(Environment.NewLine, recordLines));
        }

        return recordBlocks;
    }

    protected override bool FileShouldBeInstalled(string relativePath) =>
        filesToInstallMatcher.Match(relativePath).HasMatches;

    public abstract void Dispose();
}
