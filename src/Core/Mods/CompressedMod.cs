using SevenZip;
using static Core.Mods.IInstaller;

namespace Core.Mods;

internal class CompressedMod : Installable
{
    private readonly string archivePath;

    internal CompressedMod(string packageName, int packageFsHash, string archivePath)
        : base(packageName, packageFsHash)
    {
        this.archivePath = archivePath;
    }

    protected override IInstaller NewInstaller() => new CompressedModInstaller(archivePath);

    private class CompressedModInstaller : IInstaller
    {
        private readonly SevenZipExtractor extractor;

        public CompressedModInstaller(string archivePath)
        {
            extractor = new SevenZipExtractor(archivePath);
        }

        void IDisposable.Dispose()
        {
            extractor.Dispose();
        }

        ConfigEntries IInstaller.Install(string dstPath, ICallbacks<string> fileCallbacks)
        {
            extractor.ExtractFiles(args =>
            {
                // TODO
                args.ExtractToFile = Path.Combine(dstPath, args.ArchiveFileInfo.FileName);
            });
            return ConfigEntries.Empty; // TODO
        }
    }
}
