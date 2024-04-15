namespace Core.Mods;

public class ProcessingCallbacks<T> : IProcessingCallbacks<T>
{
    private static readonly Predicate<T> EmptyPredicate = _ => true;
    private static readonly Action<T> EmptyAction = _ => { };

    private readonly Predicate<T>? accept;
    private readonly Action<T>? before;
    private readonly Action<T>? after;

    private ProcessingCallbacks(
        Predicate<T>? accept,
        Action<T>? before,
        Action<T>? after)
    {
        this.accept = accept;
        this.before = before;
        this.after = after;
    }

    public static ProcessingCallbacks<T> Null() => new(null, null, null);

    public bool Accept(T key) => (accept ?? EmptyPredicate)(key);
    public void Before(T key) => (before ?? EmptyAction)(key);
    public void After(T key) => (after ?? EmptyAction)(key);

    public static ProcessingCallbacks<T> From(IProcessingCallbacks<T> callbacks) =>
        callbacks is ProcessingCallbacks<T>
            ? (ProcessingCallbacks<T>)callbacks
            : new ProcessingCallbacks<T>(callbacks.Accept, callbacks.Before, callbacks.After);

    public ProcessingCallbacks<T> WithAccept(Predicate<T> accept) =>
        new (Combine(this.accept, accept), before, after);

    public ProcessingCallbacks<T> WithBefore(Action<T> before) =>
        new(accept, Combine(this.before, before), after);

    public ProcessingCallbacks<T> WithAfter(Action<T> after) =>
        new(accept, before, Combine(this.after, after));

    private static Predicate<T>? Combine(Predicate<T>? p1, Predicate<T>? p2) =>
        p1 is null ? p2 : (p2 is null ? p1 : key => p1(key) && p2(key));

    private static Action<T>? Combine(Action<T>? a1, Action<T>? a2) =>
        a1 is null ? a2 : (a2 is null ? a1 : key => { a1(key); a2(key); });
}
