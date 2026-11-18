using System.IO.Abstractions;
using Gameloop.Vdf;
using Gameloop.Vdf.JsonConverter;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Core.Games;

internal sealed class SteamLibraryFolders(IFileSystem fs)
{
    public IReadOnlyList<LibraryFolder> Read(string configurationPath)
    {
        var contents = ReadContents(configurationPath);
        try
        {
            var root = VdfConvert.Deserialize(contents).ToJson();
            if (!root.Name.Equals("libraryfolders", StringComparison.OrdinalIgnoreCase) || root.Value is not JObject folders)
            {
                throw new InvalidDataException("Expected a libraryfolders object.");
            }

            return folders.Properties()
                .Where(property => uint.TryParse(property.Name, out _))
                .Select(property => ReadFolder(property.Value))
                .ToArray();
        }
        // The VDF converter throws an untyped Exception for duplicate object keys.
        catch (Exception exception) when (exception is VdfException or JsonException or InvalidDataException or ArgumentException ||
                                          exception.GetType() == typeof(Exception))
        {
            throw new InvalidDataException($"Invalid Steam library configuration '{configurationPath}'.", exception);
        }
    }

    private string ReadContents(string configurationPath)
    {
        try
        {
            return fs.File.ReadAllText(configurationPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Cannot read Steam library configuration '{configurationPath}'.", exception);
        }
    }

    private static LibraryFolder ReadFolder(JToken token)
    {
        if (token is not JObject folder || folder.GetValue("path", StringComparison.OrdinalIgnoreCase)?.Type != JTokenType.String)
        {
            throw new InvalidDataException("Expected a library folder with a path.");
        }

        var path = folder.GetValue("path", StringComparison.OrdinalIgnoreCase)?.Value<string>();
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidDataException("The library folder path is empty.");
        }

        var apps = folder.GetValue("apps", StringComparison.OrdinalIgnoreCase)?.ToObject<Dictionary<string, string>>() ??
                   new Dictionary<string, string>();
        return new LibraryFolder(path, apps);
    }

    internal sealed record LibraryFolder(string Path, IReadOnlyDictionary<string, string> Apps);
}
