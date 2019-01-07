using System.Collections.Concurrent;

namespace DotCached.Core.Helpers
{
    public static class ConcurrentDictionaryExtensions
    {
        public static TValue TryGet<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key)
        {
            return dictionary.TryGetValue(key, out var value) ? value : default(TValue);
        }
    }
}