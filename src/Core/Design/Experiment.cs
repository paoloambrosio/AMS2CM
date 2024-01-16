namespace Core.Design;

internal class Experiment
{
}

// Here...
// - identify if a mod needs configuration
// - method to concatenate all files at the root either from archive or directory for post-processing
// - trd and crd come from the file list
// - identify root subdirectories
//
// NOTES:
// - This should be either an interface or the repository should inject
//   the IFileInstaller (or perhaps an interface that implements it)
//   so that it can be testable
// - THIS SHOULD NOT BE CREATED FOR CUSTOM BOOTFILES OR THEY WOULD
//   BE CONFIGURED LIKE ANY OTHER MOD. WE SHOULD CREATE DIRECTLY AN
//   ArchiveSourceInstaller (OR ITS OWN IFileInstaller TO EXTRACT FROM GAME)
//
// TODO REDESIGN TODO REDESIGN TODO REDESIGN TODO REDESIGN TODO REDESIGN
// - it feels wrong to have ModSource implement IInstallable directly
// - identification of root directories must be shared between 7z and dirs (but now it's only 7z)
// - need to find a way to expose files that are not installable, without installing them
internal class ModSource : IInstallable
{
    public string Configuration => "TODO";

    public void Install(string targetPath, Predicate<string> beforeFile, Action<string> afterFile)
    {
        throw new NotImplementedException();
    }
}

// OK OK OK OK OK OK OK OK OK OK OK OK OK OK OK OK OK OK OK OK OK
internal interface IInstallable
{
    /// <summary>
    /// Install files into a target directory.
    /// </summary>
    /// <param name="targetPath">Target directory path where to install files.</param>
    /// <param name="beforeFile">Callback to accept or reject file before file installation.</param>
    /// <param name="afterFile">Callback after file installation.</param>
    public void Install(string targetPath, Predicate<string> beforeFile, Action<string> afterFile);
}

// This one could have the concept of file deletion, not the ModSource
// or IPackageInstaller, so that it can be used for bootfiles as well
// TODO REDESIGN TODO REDESIGN TODO REDESIGN TODO REDESIGN TODO REDESIGN
// TODO REDESIGN TODO REDESIGN TODO REDESIGN TODO REDESIGN TODO REDESIGN
// TODO REDESIGN TODO REDESIGN TODO REDESIGN TODO REDESIGN TODO REDESIGN
internal class ModInstaller
{
    private readonly string targetPath;

    public ModInstaller(string targetPath)
    {
        this.targetPath = targetPath;
    }

    public ModInstaller Include(Predicate<string> inclusionFilter)
    {
        inclusionFilters.Add(inclusionFilter);
        return this;
    }

    public InstallationState Uninstall(InstallationState currentState, Predicate<string> forEach)
    {
        throw new NotImplementedException();
    }

    public InstallationState Install(IFileInstaller fileInstaller, Predicate<string> forEach)
    {
        var installedFiles = new List<string>();
        fileInstaller.Extract(targetPath, TrackIncludedFiles(installedFiles));
        throw new NotImplementedException();
    }

    private Predicate<string> TrackIncludedFiles(IList<string> includedFiles)
    {
        // TODO this should also
        // - backup the file
        // - skip it if it should be deleted
        // - remove the deletion file suffix before tracking the installed file and calling filters
        bool TrackIncluded(string filePath) {
            includedFiles.Add(filePath);
            return true;
        }
        return inclusionFilters.Append(TrackIncluded).Aggregate(Predicate.And<string>);
    }
}

public static class Predicate {
    public static Predicate<T> And<T>(this Predicate<T> left, Predicate<T> right)
    {
        return a => left(a) && right(a);
    }
}

public record InstallationState
(
    bool Successful,
    IReadOnlyCollection<string> InstalledFiles
)
{
    public bool Installed => InstalledFiles.Any();
}

// This can be implemented for archives, filesystem or even pakfiles without
// extracting them, thus removing the need for a temporary directory
// Shall we have something like what 7z has where we pass a struct (or perhaps
// a union type https://spencerfarley.com/2021/03/26/unions-in-csharp/#short-version)
internal interface IFileInstaller
{
    public void Extract(string targetPath, Predicate<string> before);

    internal interface IExtractCallback
    {
        internal class Before : IExtractCallback
        {
            public string? TargetPath { get; set; }
        }

        internal record After : IExtractCallback
        {
            public string? TargetPath { get; }
        }
    }
}

// 7zip's ExtractFiles methods has a callback where you can specify what to do with the file
// - ExtractToFile to specify the directory to extract it to (or nothing to skip it)
// - ExtractToStream could be used to route readmes to parse config
// - Exception when an error occurres and CancelExtraction to stop extraction
// Notes:
// - there is a reason parameter so it's called before and after
// - it's annoying that we can have several mod roots and that we need to identify them based
//   on directory names; we don't want to build it while we extract files in case we see first
//   other files that are at a root but we haven't seen a directory yet

internal class ArchiveSourceInstaller : IFileInstaller
{
    public ArchiveSourceInstaller(IList<string> rootPaths)
    {
        throw new NotImplementedException();
    }

    public void Extract(string targetPath, Predicate<string> before)
    {

    }
}

internal class DirectorySourceInstaller : IFileInstaller
{
    public DirectorySourceInstaller(IList<string> rootPaths)
    {
        throw new NotImplementedException();
    }

    public void Extract(string targetPath, Predicate<string> before)
    {

    }
}
