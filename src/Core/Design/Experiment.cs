using Core.Mods;
using Core.State;

namespace Core.Design;

/* TODO
 * - wrappers don't work!!!
 * - backup should be done by another wrapper that calls the
 *   collaborator that handles backup and restore, not by the
 *   installer itself
 * - how do we handle file deletion? We should probably pass a record
 *   with operation, path, etc. instead of string to the callback?
 *   actually a deletion is not special at the moment: it can be
 *   abstracted inside the mod (part of the single mod installer, not
 *   the generic ModPackageInstaller)
 */


/* Note that some files (trd, crd, ...) are excluded from config */
public class IModPackage : IInstallable<string, ConfigEntries>
{

    // IInstaller<ModConfig> Installer() {
    //   // It returns the right installer for the kind of package
    //   throw new NotImplementedException();
    // }
    public string Name => throw new NotImplementedException();

    public ConfigEntries Install(IInstallable<string, ConfigEntries>.Callbacks callbacks) => throw new NotImplementedException();
}

class ModPackageInstaller // : IPackageInstaller
{
    // All features like bootfiles awareness, backup, etc. are
    // implmented as plugins to be tested independently.
    interface InstallerExtension
    {
        void initApply(
          out Dictionary<string, InternalModInstallationState> currentState,
          out IEnumerable<IModPackage> desiredPackages
        );
    }

    private List<InstallerExtension> extensions;

    // TODO this doesn't allow the service to add callbacks for logging, etc.
    public Dictionary<string, InternalModInstallationState> Apply(
        Dictionary<string, InternalModInstallationState> currentState,
        IEnumerable<IModPackage> desiredPackages)
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
