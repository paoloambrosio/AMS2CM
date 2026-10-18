using System.IO.Abstractions;
using System.Security.Cryptography;
using Core.Utils;

namespace Core.Packages.Installation;

public sealed record InstalledFileState(bool Exists, long Length, DateTime CreationTimeUtc, DateTime LastWriteTimeUtc)
{
    // Absent on older saved states, which contain ordinary metadata snapshots.
    public bool MetadataUnavailable { get; init; }
    public string? ContentHash { get; init; }

    internal static InstalledFileState Unknown { get; } = new(true, 0, default, default)
        { MetadataUnavailable = true };

    internal static InstalledFileState Capture(IFileSystem fs, RootedPath path)
    {
        var file = fs.FileInfo.New(path.Full);
        return file.Exists
            ? new(true, file.Length, file.CreationTimeUtc, file.LastWriteTimeUtc)
            : new(false, 0, default, default);
    }

    internal static InstalledFileState CaptureAfterWrite(IFileSystem fs, RootedPath path)
    {
        try
        {
            return Capture(fs, path);
        }
        catch (Exception metadataError) when (metadataError is IOException or UnauthorizedAccessException)
        {
            try
            {
                return new(true, 0, default, default)
                {
                    MetadataUnavailable = true,
                    ContentHash = HashContents(fs, path)
                };
            }
            catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException)
            {
                return new(false, 0, default, default);
            }
            catch (Exception readError) when (readError is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Could not record the installed contents of {path.Full}. " +
                    "The file and any original backup have been retained.", new AggregateException(metadataError, readError));
            }
        }
    }

    internal bool Matches(IFileSystem fs, RootedPath path)
    {
        if (!MetadataUnavailable) return this == Capture(fs, path);
        if (ContentHash is null)
            throw new IOException($"Cannot safely reconcile {path.Full}: its installed contents could not be recorded. " +
                "The file and any original backup have been retained; manual recovery is required.");
        try
        {
            return ContentHash == HashContents(fs, path);
        }
        catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string HashContents(IFileSystem fs, RootedPath path)
    {
        using var stream = fs.File.OpenRead(path.Full);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
