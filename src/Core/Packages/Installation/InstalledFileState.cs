using System.IO.Abstractions;
using Core.Utils;

namespace Core.Packages.Installation;

public sealed record InstalledFileState(bool Exists, long Length, DateTime CreationTimeUtc, DateTime LastWriteTimeUtc)
{
    internal static InstalledFileState Capture(IFileSystem fs, RootedPath path)
    {
        var file = fs.FileInfo.New(path.Full);
        return file.Exists
            ? new(true, file.Length, file.CreationTimeUtc, file.LastWriteTimeUtc)
            : new(false, 0, default, default);
    }

    internal bool Matches(IFileSystem fs, RootedPath path) => this == Capture(fs, path);
}
