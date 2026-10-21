using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Core.Packages.Installation;
using Core.Utils;
using Newtonsoft.Json;

namespace Core.State.JsonFile;

internal class JsonFileStatePersistence : IStatePersistence
{
    internal const int UnkownVersionHash = 42;

    private const string StateV2FileName = "state.json";
    private const string StateV1FileName = "installed.json";

    private IFile File => stateV2FilePath.FileSystem.File;
    private readonly IFileInfo stateV2FilePath;
    private readonly IFileInfo stateV1FilePath;

    private static readonly JsonSerializerSettings JsonSerializerSettings = new()
    {
        Formatting = Formatting.None,
        DefaultValueHandling = DefaultValueHandling.Ignore,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc
    };

    public JsonFileStatePersistence(IDirectoryInfo modsPath) :
        this(modsPath.FileSystem.FileInfo.New(modsPath.FileSystem.Path.Combine(modsPath.FullName, StateV2FileName)),
            modsPath.FileSystem.FileInfo.New(modsPath.FileSystem.Path.Combine(modsPath.FullName, StateV1FileName)))
    {
    }

    internal JsonFileStatePersistence(IFileInfo stateV2FilePath, IFileInfo stateV1FilePath)
    {
        this.stateV2FilePath = stateV2FilePath;
        this.stateV1FilePath = stateV1FilePath;
    }

    // The beauty of JSON libraries setting non-null fields to null
    [SuppressMessage("ReSharper", "NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract")]
    public SavedState ReadState()
    {
        if (File.Exists(stateV2FilePath.FullName))
        {
            var jsonState = ReadState<JsonSavedStateV2>(stateV2FilePath.FullName);
            if (jsonState is null)
            {
                return SavedState.Empty();
            }

            var installTime = jsonState.Install.Time ?? File.GetLastWriteTimeUtc(stateV2FilePath.FullName);
            return new SavedState(
                Installation: jsonState.Install.Mods.SelectValues(pis => new PackageInstallationState(
                    Time: pis.Time == default ? installTime : pis.Time,
                    VersionHash: pis.VersionHash,
                    Partial: pis.Partial,
                    Dependencies: pis.Dependencies ?? Array.Empty<string>(),
                    Files: pis.Files ?? Array.Empty<string>(),
                    ShadowedBy: pis.ShadowedBy ?? Array.Empty<string>())
                ).ToImmutableDictionary()
            );
        }

        if (File.Exists(stateV1FilePath.FullName))
        {
            var jsonState = ReadState<Dictionary<string, IReadOnlyCollection<string>>>(stateV1FilePath.FullName);
            if (jsonState is null)
            {
                return SavedState.Empty();
            }

            var installTime = File.GetLastWriteTimeUtc(stateV1FilePath.FullName);
            return new SavedState(
                Installation: jsonState.AsEnumerable().ToDictionary(
                    kv => kv.Key,
                    kv => new PackageInstallationState(
                        Time: installTime,
                        VersionHash: UnkownVersionHash, // Fake hash to force reinstall on older versions
                        Partial: false,
                        Dependencies: Array.Empty<string>(),
                        Files: kv.Value, ShadowedBy: Array.Empty<string>())
                )
            );
        }

        return SavedState.Empty();
    }

    private T? ReadState<T>(string stateFile)
    {
        string contents;
        try
        {
            contents = File.ReadAllText(stateFile);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to read state file", ex);
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(contents, JsonSerializerSettings);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to decode state file", ex);
        }
    }

    public void WriteState(SavedState state)
    {
        var jsonState = new JsonSavedStateV2(
            Install: new JsonInstallationStateV2(
                Time: null,
                Mods: state.Installation.SelectValues(pis => new JsonPackageInstallationStateV2(
                    Time: pis.Time.UtcDateTime,
                    VersionHash: pis.VersionHash,
                    Partial: pis.Partial,
                    Dependencies: pis.Dependencies,
                    Files: pis.Files,
                    ShadowedBy: pis.ShadowedBy
                ))
            ));

        // Remove state v1 on write if upgrading from a previous version
        File.Delete(stateV1FilePath.FullName);

        try
        {
            File.WriteAllText(stateV2FilePath.FullName, JsonConvert.SerializeObject(jsonState, JsonSerializerSettings));
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to write state V2 file", ex);
        }
    }
}
