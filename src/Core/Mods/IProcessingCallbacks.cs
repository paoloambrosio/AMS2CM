namespace Core.Mods;

public interface IProcessingCallbacks<T>
{
    /// <summary>
    /// Decide if an entry should be processed.
    /// </summary>
    /// <param name="key">Entry key</param>
    /// <returns>Should be processed</returns>
    public bool Accept(T key);

    /// <summary>
    /// Called before processing an entry.
    /// </summary>
    /// <param name="key">Entry key</param>
    public void Before(T key);

    /// <summary>
    /// Called after processing an entry.
    /// </summary>
    /// <param name="key">Entry key</param>
    public void After(T key);
}
