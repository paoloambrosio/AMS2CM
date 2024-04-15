using static Core.Mods.IInstaller;

namespace Core.Mods;

public abstract class ExtractedMod : Installable
{
    private readonly ModInstaller.IConfig config;
    private readonly string extractedPath;

    internal ExtractedMod(string packageName, int? packageFsHash, ModInstaller.IConfig config, string extractedPath)
    : base(packageName, packageFsHash)
    {
        this.extractedPath = extractedPath;
        this.config = config;
    }

    protected override IInstaller NewInstaller() => new Installer(config, extractedPath);

    private class Installer : ModInstaller
    {
        private readonly string extractedPath;

        public Installer(ModInstaller.IConfig config, string extractedPath) :
            base(config)
        {
            this.extractedPath = extractedPath;
        }

        public override ConfigEntries Install(string dstPath, ICallbacks<string> fileCallbacks)
        {
            foreach (var rootPath in RootPaths())
            {
                // TODO MOVE THIS HERE AND COMMON IN SUPERCLASS
                JsgmeFileInstaller.InstallFiles(rootPath, dstPath, fileCallbacks.Accept, fileCallbacks.After);
            }
            return GenerateConfig();
        }

        private List<string> FindDrivelineRecords()
        {
            var recordBlocks = new List<string>();
            foreach (var fileAtModRoot in Directory.EnumerateFiles(extractedPath))
            {
                using var stream = File.OpenRead(fileAtModRoot);
                recordBlocks.AddRange(FindDrivelineRecords(stream));
            }
            return recordBlocks;
        }

        protected override IEnumerable<string> RootPaths()
        {
            return FindRootContaining(extractedPath, dirsAtRootLowerCase);
        }

        private static List<string> FindRootContaining(string path, IEnumerable<string> contained)
        {
            var roots = new List<string>();
            foreach (var subdir in Directory.GetDirectories(path))
            {
                var localName = Path.GetFileName(subdir).ToLowerInvariant();
                if (contained.Contains(localName))
                {
                    return new List<string> { path };
                }
                roots.AddRange(FindRootContaining(subdir, contained));
            }

            return roots;
        }

        public override void Dispose() => throw new NotImplementedException();
    }
}