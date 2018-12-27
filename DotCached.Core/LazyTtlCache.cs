using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DotCached.Core
{
    
    public class LazyTtlCache<TKey, TValue>
        where TValue : class
    {
        private readonly ConcurrentDictionary<TKey, ExpiringValue> _cache =
            new ConcurrentDictionary<TKey, ExpiringValue>();

        private readonly IValueFactory<TKey, TValue> _valueFactory;
        public readonly TimeSpan Ttl;

        public LazyTtlCache(TimeSpan ttl, IValueFactory<TKey, TValue> valueFactory)
        {
            Ttl = ttl;
            _valueFactory = valueFactory;
        }

        public async Task<TValue> GetOrNull(TKey key)
        {
            var expiringValue = _cache.GetOrAdd(key, k => new ExpiringValue(null, DateTimeOffset.MinValue));
            if (expiringValue.Expiration > DateTimeOffset.Now) return expiringValue.Value;

            await expiringValue.WriterSemaphore.WaitAsync();
            try
            {
                if (_cache[key].Expiration <= DateTimeOffset.Now)
                {
                    Set(key, await _valueFactory.Get(key));
                }
            }
            catch (Exception ex)
            {
                //TODO: parametrize this strategy    
            }
            finally
            {
                expiringValue.WriterSemaphore.Release();
            }

            return _cache[key].Value;
        }

        public void Set(TKey key, TValue value)
        {
            var expiringValue = new ExpiringValue(value, DateTimeOffset.UtcNow + Ttl);
            _cache.AddOrUpdate(key, expiringValue, (_, __) => expiringValue);
        }

        internal class CacheValue
        {
            public CacheValue()
            {
                WriterSemaphore = new SemaphoreSlim(1, 1);
            }

            public SemaphoreSlim WriterSemaphore { get; }
        }

        internal class ExpiringValue : CacheValue
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