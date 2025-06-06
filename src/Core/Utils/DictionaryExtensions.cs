﻿namespace Core.Utils;

public static class DictionaryExtensions
{
    public static IDictionary<TKey, TValue> Merge<TKey, TValue>(this IDictionary<TKey, TValue> d1, IDictionary<TKey, TValue> d2)
        where TKey : notnull
    {
        return d1.Concat(d2).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public static IDictionary<TKey, TValueOut> SelectValues<TKey, TValueIn, TValueOut>(this IDictionary<TKey, TValueIn> dict, Func<TValueIn, TValueOut> f)
        where TKey : notnull
    {
        return dict.ToDictionary(kv => kv.Key, kv => f(kv.Value));
    }

    public static IReadOnlyDictionary<TKey, TValueOut> SelectValues<TKey, TValueIn, TValueOut>(this IReadOnlyDictionary<TKey, TValueIn> dict, Func<TValueIn, TValueOut> f)
        where TKey : notnull
    {
        return dict.ToDictionary(kv => kv.Key, kv => f(kv.Value));
    }

    public static IDictionary<TKey, TValueOut> SelectValues<TKey, TValueIn, TValueOut>(this IDictionary<TKey, TValueIn> dict, Func<TKey, TValueIn, TValueOut> f)
        where TKey : notnull
    {
        return dict.ToDictionary(kv => kv.Key, kv => f(kv.Key, kv.Value));
    }

    public static IReadOnlyDictionary<TKey, TValueOut> SelectValues<TKey, TValueIn, TValueOut>(this IReadOnlyDictionary<TKey, TValueIn> dict, Func<TKey, TValueIn, TValueOut> f)
        where TKey : notnull
    {
        return dict.ToDictionary(kv => kv.Key, kv => f(kv.Key, kv.Value));
    }

    public static void Upsert<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TValue, TValue> updatedValue, Func<TValue> insertedValue)
        where TKey : notnull
    {
        dict[key] = dict.TryGetValue(key, out var existing) ? updatedValue(existing) : insertedValue();
    }
}
