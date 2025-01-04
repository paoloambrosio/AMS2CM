using Core.Backup;
using Core.Mods;

namespace Core;
internal class BootfilesMod : IInstaller
{
    public interface IEventHandler
    {
        void PostProcessingNotRequired();
        void PostProcessingStart();
        void ExtractingBootfiles();
        void PostProcessingVehicles();
        void PostProcessingTracks();
        void PostProcessingDrivelines();
        void PostProcessingEnd();
    }

    private readonly IInstaller inner;
    private readonly IEventHandler eventHandler;
    private bool postProcessingDone;

    public BootfilesMod(IInstaller inner, IEventHandler eventHandler)
    {
        this.inner = inner;
        this.eventHandler = eventHandler;
        postProcessingDone = false;
    }

    public string PackageName => inner.PackageName;

    public IInstallation.State Installed =>
        inner.Installed == IInstallation.State.Installed && !postProcessingDone
            ? IInstallation.State.PartiallyInstalled
            : inner.Installed;

    public IReadOnlyCollection<string> InstalledFiles => inner.InstalledFiles;

    public int? PackageFsHash => inner.PackageFsHash;

    public ConfigEntries Install(string dstPath, IInstallationBackupStrategy backupStrategy, ProcessingCallbacks<RootedPath> callbacks)
    {
        inner.Install(dstPath, backupStrategy, callbacks);
        return ConfigEntries.Empty;
    }

    private BootfilesMod CreateBootfilesMod(IReadOnlyCollection<ModPackage> packages, IEventHandler eventHandler)
    {
        var bootfilesPackage = packages.FirstOrDefault(p => IsBootFiles(p.PackageName));
        if (bootfilesPackage is null)
        {
            eventHandler.ExtractingBootfiles(null);
            return new BootfilesMod(installationFactory.GeneratedBootfilesInstaller(), eventHandler);
        }
        eventHandler.ExtractingBootfiles(bootfilesPackage.PackageName);
        return new BootfilesMod(installationFactory.ModInstaller(bootfilesPackage), eventHandler);
    }

    public void PostProcessing(string dstPath, IReadOnlyList<ConfigEntries> modConfigs)
    {
        eventHandler.PostProcessingVehicles();
        PostProcessor.AppendCrdFileEntries(dstPath, modConfigs.SelectMany(c => c.CrdFileEntries));
            eventHandler.PostProcessingTracks();
            PostProcessor.AppendTrdFileEntries(dstPath, modConfigs.SelectMany(c => c.TrdFileEntries));
            eventHandler.PostProcessingDrivelines();
            PostProcessor.AppendDrivelineRecords(dstPath, modConfigs.SelectMany(c => c.DrivelineRecords));
            postProcessingDone = true;
        }
    }
}
