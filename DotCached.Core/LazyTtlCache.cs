using System;
using System.Collections.Concurrent;

namespace DotCached.Core
{
    public class LazyTtlCache<TKey, TValue>
        where TKey : class
        where TValue : class
    {
        private readonly ConcurrentDictionary<TKey, ExpiringValue<TValue>> _cache = 
            new ConcurrentDictionary<TKey, ExpiringValue<TValue>>();

        private readonly IValueFactory<TKey, TValue> _valueFactory;
        public readonly TimeSpan Ttl;

        public LazyTtlCache(TimeSpan ttl, IValueFactory<TKey, TValue> valueFactory)
        {
            Ttl = ttl;
            _valueFactory = valueFactory;
        }

        public TValue GetOrNull(TKey key)
        {
//            if (!_cache.TryGetValue(key, out var existingValue))
//            {
                return _valueFactory.Get(key);
//            }
        }
        public void Set(TKey key, TValue value)
        {
            var expiringValue = new ExpiringValue<TValue>(value, DateTimeOffset.UtcNow + Ttl);
            _cache.AddOrUpdate(key, expiringValue, (_, __) => expiringValue);
        }

        public TValue this[TKey key]
        {
            get { return this.GetOrNull(key); }
            set { this.Set(key, value); }

        }

        internal class ExpiringValue<TValue>
        {
            public ExpiringValue(TValue value, DateTimeOffset expiration)
            {
                Value = value;
                Expiration = expiration;
            }

            public TValue Value { get; }
            public DateTimeOffset Expiration { get; }
        }
    }
}
