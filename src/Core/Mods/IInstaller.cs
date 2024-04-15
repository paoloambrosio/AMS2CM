namespace Core.Mods;

public interface IInstaller : IDisposable
{
    public interface ICallbacks<T>
    {
        public bool Accept(T key);
        public void Before(T key);
        public void After(T key);
    }

    public ConfigEntries Install(string dstPath, ICallbacks<string> fileCallbacks);
}