using System.IO.Abstractions;

namespace Core.Utils;

public class SubdirectoryTempDir : ITempDir
{
    private const string TempDirName = "Temp";
    private readonly IFileSystem fileSystem;

    public SubdirectoryTempDir(IFileSystem fileSystem, string parentPath)
    {
        BasePath = Path.Combine(parentPath, TempDirName);
        this.fileSystem = fileSystem;
    }

    public string BasePath
    {
        get;
    }

    public void Cleanup()
    {
        if (fileSystem.Directory.Exists(BasePath))
        {
            fileSystem.Directory.Delete(BasePath, recursive: true);
        }
    }
}
