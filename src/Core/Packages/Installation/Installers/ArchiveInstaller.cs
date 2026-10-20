using System.IO.Abstractions;
using Core.Utils;
using LibArchive.Net;

namespace Core.Packages.Installation.Installers;

internal class ArchiveInstaller : BaseInstaller<Stream>
{
    private const uint BlockSize = 1 << 23;
    private const int CopyBufferSize = 1 << 27;

    private readonly string archivePath;

    public ArchiveInstaller(IFileSystem fileSystem, TimeProvider timeProvider, PackageMetadata package,
        string archivePath) :
        base(fileSystem, timeProvider, package)
    {
        this.archivePath = archivePath;
    }

    // LibArchive.Net is a mere wrapper around libarchive. It's better to avoid using
    // LINQ expressions as they can lead to <see cref="LibArchiveReader.Entries"/> or
    // <see cref="LibArchiveReader.Entry.Stream"/> being called out of order.

    public override IEnumerable<string> RelativeDirectoryPaths
    {
        get
        {
            using var archiveStream = FileSystem.File.OpenRead(archivePath);
            using var reader = new LibArchiveReader(archiveStream, blockSize: BlockSize);
            var ret = new HashSet<string>();
            foreach (var entry in reader.Entries())
            {
                // Not all archive types store directories as separate entries
                // We consider files and find their parent directory
                if (!entry.IsRegularFile)
                {
                    continue;
                }
                var normalizedPath = Path.GetDirectoryName(entry.Name);
                if (normalizedPath is not null)
                {
                    ret.Add(normalizedPath);
                }
            }
            return ret;
        }
    }

    protected override void InstallAllFiles(InstallBody body)
    {
        using var archiveStream = FileSystem.File.OpenRead(archivePath);
        using var reader = new LibArchiveReader(archiveStream, blockSize: BlockSize);
        foreach (var entry in reader.Entries())
        {
            if (!entry.IsRegularFile)
            {
                continue;
            }
            var normalizedPath = Paths.NormalizeDirectorySeparators(entry.Name);
            body(normalizedPath, entry.Stream);
        }
    }


    protected override void InstallFile(RootedPath? destinationPath, Stream stream)
    {
        if (destinationPath is null)
        {
            return;
        }

        FileSystem.Directory.GetParent(destinationPath.Full)?.Create();
        using var destinationStream = FileSystem.FileStream.New(destinationPath.Full, FileMode.Create);
        stream.CopyTo(destinationStream, CopyBufferSize);
    }
}
