using System.IO.Abstractions;
using System.Runtime.Versioning;

namespace Core.Games;

[SupportedOSPlatform("linux")]
public class LinuxSteam : ISteamLibraryLocator
{
    private readonly IFileSystem fs;
    private readonly string homeDirectory;
    private readonly string? dataDirectory;

    public LinuxSteam(IFileSystem fs) : this(
        fs, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetEnvironmentVariable("XDG_DATA_HOME"))
    {
    }

    internal LinuxSteam(IFileSystem fs, string homeDirectory, string? dataDirectory)
    {
        this.fs = fs;
        this.homeDirectory = homeDirectory;
        this.dataDirectory = dataDirectory;
    }

    public string AppLibraryPath(string appId)
    {
        var configurations = FindConfigurations().ToArray();
        if (configurations.Length == 0)
        {
            throw new InvalidOperationException("Cannot find Steam library configuration. Specify an absolute Game:Path.");
        }

        var reader = new SteamLibraryFolders(fs);
        var libraries = configurations.SelectMany(configurationPath => reader.Read(configurationPath)
                .Where(folder => folder.Apps.ContainsKey(appId))
                .Select(folder => CanonicalLibraryPath(configurationPath, folder.Path)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return libraries.Length switch
        {
            0 => throw new InvalidOperationException(
                $"Cannot find app ID {appId} in any Steam library. Specify an absolute Game:Path."),
            1 => libraries[0],
            _ => throw new InvalidOperationException(
                $"App ID {appId} found in multiple Steam libraries: {string.Join(", ", libraries)}. Specify an absolute Game:Path.")
        };
    }

    private IEnumerable<string> FindConfigurations()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in InstallationRoots())
        {
            var configurationPath = fs.Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!ConfigurationExists(configurationPath))
            {
                continue;
            }

            var canonicalDirectory = CanonicalDirectory(fs.Path.Combine(root, "steamapps"));
            configurationPath = fs.Path.Combine(canonicalDirectory, "libraryfolders.vdf");
            var file = fs.FileInfo.New(configurationPath);
            if (file.LinkTarget is not null)
            {
                configurationPath = file.ResolveLinkTarget(true)?.FullName ??
                                    throw new IOException($"Cannot resolve Steam configuration '{configurationPath}'.");
            }
            if (seen.Add(configurationPath))
            {
                yield return configurationPath;
            }
        }
    }

    private IEnumerable<string> InstallationRoots()
    {
        if (!fs.Path.IsPathFullyQualified(homeDirectory))
        {
            throw new InvalidOperationException("Cannot find the user home directory. Specify an absolute Game:Path.");
        }

        yield return fs.Path.Combine(homeDirectory, ".steam", "steam");
        yield return fs.Path.Combine(homeDirectory, ".steam", "root");
        yield return fs.Path.Combine(homeDirectory, ".steam", "debian-installation");
        if (dataDirectory is not null && fs.Path.IsPathFullyQualified(dataDirectory))
        {
            yield return fs.Path.Combine(dataDirectory, "Steam");
        }
        yield return fs.Path.Combine(homeDirectory, ".local", "share", "Steam");
        yield return fs.Path.Combine(homeDirectory, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        yield return fs.Path.Combine(homeDirectory, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
        yield return fs.Path.Combine(homeDirectory, "snap", "steam", "common", ".local", "share", "Steam");
        yield return fs.Path.Combine(homeDirectory, ".snap", "data", "steam", "common", ".local", "share", "Steam");
    }

    private bool ConfigurationExists(string configurationPath)
    {
        FileAttributes attributes;
        try
        {
            attributes = fs.File.GetAttributes(configurationPath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Cannot access Steam library configuration '{configurationPath}'.", exception);
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new InvalidDataException($"Steam library configuration '{configurationPath}' is a directory.");
        }
        return true;
    }

    private string CanonicalLibraryPath(string configurationPath, string path)
    {
        if (!fs.Path.IsPathFullyQualified(path))
        {
            throw new InvalidDataException($"Steam library path '{path}' in '{configurationPath}' must be absolute.");
        }
        return CanonicalDirectory(path);
    }

    private string CanonicalDirectory(string path)
    {
        path = fs.Path.TrimEndingDirectorySeparator(fs.Path.GetFullPath(path));
        var parent = fs.Directory.GetParent(path);
        if (parent is null)
        {
            return path;
        }

        var canonicalParent = CanonicalDirectory(parent.FullName);
        var candidate = fs.Path.Combine(canonicalParent, fs.Path.GetFileName(path));
        if (!fs.Directory.Exists(candidate))
        {
            return candidate;
        }

        var directory = fs.DirectoryInfo.New(candidate);
        if (directory.LinkTarget is null)
        {
            return candidate;
        }

        var target = directory.ResolveLinkTarget(true) ?? throw new IOException($"Cannot resolve Steam directory '{candidate}'.");
        return CanonicalDirectory(target.FullName);
    }
}
