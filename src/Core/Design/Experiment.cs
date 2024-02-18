using Core.Mods;
using Core.State;
using Octokit;

namespace Core.Design;

/* TODO
 * - wrappers do work but they are not really composable
 *   - if backup is not a wrapper or an extension but in the main
 *     package installer, bootfiles aware installer could be a wrapper
 *   - ...could also have a bootfiles-aware store that always
 *     returns bootfiles (or depending on the method?); the
 *     bootfiles-aware installer will make sure that bootfiles are
 *     at the beginning/top (last when reversed) and skip
 *     installation if not needed
 * - ~backup should be done by another wrapper that calls the
 *   collaborator that handles backup and restore, not by the
 *   installer itself~
 * - how do we handle file deletion? ~We should probably pass a record
 *   with operation, path, etc. instead of string to the callback?~
 *   actually a deletion is not special at the moment: it can be
 *   abstracted inside the mod (part of the single mod installer, not
 *   the generic ModPackageInstaller)
 * - NOT SURE WHERE THE STATE SHOULD BE
 *   - if in the service then it would have to know about bootfiles
 *     to mark mods out of date when bootfiles are out of date
 */

public class Mod
{
    private string FilePath => throw new NotImplementedException();

    public string FileHash => throw new NotImplementedException();

    public PackageName PackageName => throw new NotImplementedException();
    public ModPackage Package => throw new NotImplementedException();
}

public record PackageName(
    string Name,
    string Version
);

/* Note that some files (trd, crd, ...) are excluded from config */
public class ModPackage : IDisposable, IInstallable<string, ConfigEntries>
{
    // IInstaller<ModConfig> Installer() {
    //   // It returns the right installer for the kind of package
    //   throw new NotImplementedException();
    // }
    public string ContentHash => throw new NotImplementedException();

    public ConfigEntries Install(IInstallable<string, ConfigEntries>.Callbacks callbacks) => throw new NotImplementedException();

    public void Dispose() => throw new NotImplementedException();
}

    class ModPackageInstaller // : IPackageInstaller
{
    // All features like bootfiles awareness, backup, etc. are
    // implmented as plugins to be tested independently.
    interface InstallerExtension
    {
        void initApply(
          out Dictionary<string, InternalModInstallationState> currentState,
          out IEnumerable<ModPackage> desiredPackages
        );
    }

    private List<InstallerExtension> extensions;

    // TODO this doesn't allow the service to add callbacks for logging, etc.
    public Dictionary<string, InternalModInstallationState> Apply(
        Dictionary<string, InternalModInstallationState> currentState,
        IEnumerable<ModPackage> desiredPackages)
    {
        throw new NotImplementedException();
    }
}

/*********************************************
 * THIS INTERFACE IS TERRIBLE!!!!!!!
 *
 * Callbacks:
 * - cancellation token
 * - logs
 * - update progress
 *********************************************/
//interface IPackageInstaller<TPackage, TState, TId, TConfig> where Package : IInstallable<TId, TConfig>
//{
//    IReadOnlyDictionary<TId, TState> Install(IEnumerable<TPackage> packages)
//}

/*********************************************
* Filters
* - skip already installed file
* - exclude files (dll, exe)
* - backup (and later restore?)
*********************************************/
public interface IInstallable<TId, TConfig>
{
    public TId Name { get; }

    // IInstaller<T> Installer();

    /*********************************************
    * Filters
    * - skip already installed file
    * - exclude files (dll, exe)
    * - backup (and later restore?)
    *********************************************/
    public interface Callbacks
    {
        public Predicate<string> BeforeEachFile { get; }
        public Action<string> AfterEachFile { get; }
    }
    public TConfig Install(Callbacks callbacks); // This version makes wrapping easier
}

// interface IInstaller<T> : IDisposable {
//   IInstaller BeforeEachFile(Predicate<string> predicate);
//   IInstaller AfterEachFile(Action<string> action);
//   T Install();
//   interface Callbacks {
//     Predicate<string> BeforeEachFile;
//     Action<string> AfterEachFile;
//   }
// }
