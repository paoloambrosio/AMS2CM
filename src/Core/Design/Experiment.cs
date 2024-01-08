namespace Core.Design;

internal class Experiment
{
}

// Here...
// - identify if a mod needs configuration
// - method to concatenate all files at the root either from archive or directory for post-processing
// - trd and crd come from the file list
//
// NOTES:
// - This should be either an interface or the repository should inject
//   the IFileInstaller (or perhaps an interface that implements it)
//   so that it can be testable
// - THIS SHOULD NOT BE CREATED FOR CUSTOM BOOTFILES OR THEY WOULD
//   BE CONFIGURED LIKE ANY OTHER MOD. WE SHOULD CREATE DIRECTLY AN
//   ArchiveSourceInstaller (OR ITS OWN IFileInstaller TO EXTRACT FROM GAME)
internal class ModSource
{
    public ModInstaller Installer()
    {
        // Here...
        // - identify root subdirectories
        // - create the right IFileInstaller based on source type (zip file or a directory)
        throw new NotImplementedException();
    }
}

// This one could have the concept of file deletion, not the ModSource
// or IPackageInstaller, so that it can be used for bootfiles as well
internal class ModInstaller
{
    private readonly IFileInstaller fileInstaller;
    private readonly List<Predicate<string>> inclusionFilters;

    public ModInstaller(IFileInstaller fileInstaller)
    {
        this.fileInstaller = fileInstaller;
    }

    public ModInstaller Include(Predicate<string> inclusionFilter)
    {
        inclusionFilters.Add(inclusionFilter);
        return this;
    }

    public InstallationState Install(string targetPath)
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
//    InstallationResult Result,
    bool Successful,
    IReadOnlyCollection<string> InstalledFiles
);
//{
//    public bool Successful => Result == InstallationResult.Installed;
//}

// THIS IS NOT NEEDED ANYMORE BECAUSE PARTIAL INSTALL IS RELEVANT ONLY AT THE SERVICE LEVEL
//public enum InstallationResult
//{
//    NotInstalled = 0,
//    PartiallyInstalled = 1,
//    Installed = 2
//}

// This can be implemented for archives, filesystem or even pakfiles without
// extracting them, thus removing the need for a temporary directory
// Shall we have something like what 7z has where we pass a struct (or perhaps
// a union type https://spencerfarley.com/2021/03/26/unions-in-csharp/#short-version)
internal interface IFileInstaller
{
    public void Extract(string targetPath, Predicate<string> before);
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
