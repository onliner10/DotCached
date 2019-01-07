using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DotCached.Core.Helpers;
using DotCached.Core.Infrastructure;
using DotCached.Core.Logging;

[assembly: InternalsVisibleTo("DotCached.Core.UnitTests")]

namespace DotCached.Core
{
    public class DotCache<TKey, TValue>
        where TValue : class
    {
        private static readonly ILog Logger = LogProvider.For<DotCache<TKey, TValue>>();

        private readonly ConcurrentDictionary<TKey, WriteableCacheValue<TValue>> _cache =
            new ConcurrentDictionary<TKey, WriteableCacheValue<TValue>>();


        private readonly IInvalidationStrategy<TValue> _invalidationStrategy;

        private readonly TimeProvider _timeProvider;

        private readonly IValueFactory<TKey, TValue> _valueFactory;

        public DotCache(IValueFactory<TKey, TValue> valueFactory, IInvalidationStrategy<TValue> invalidationStrategy)
            : this(valueFactory, invalidationStrategy, true, TimeProviders.Default)
        {
        }

        internal DotCache(IValueFactory<TKey, TValue> valueFactory, IInvalidationStrategy<TValue> invalidationStrategy,
            bool allowStale, TimeProvider timeProvider)
        {
            AllowStale = allowStale;
            _valueFactory = valueFactory;
            _invalidationStrategy = invalidationStrategy;
            _timeProvider = timeProvider;
        }

        public int Count => _cache.Count;
        public bool AllowStale { get; }

        public virtual async Task<TValue> GetOrNull(TKey key)
        {
            var expiringValue =
                _cache.GetOrAdd(key, k => new WriteableCacheValue<TValue>(null, DateTimeOffset.MinValue));
            if (expiringValue.Value != null && !_invalidationStrategy.ShouldInvalidate(expiringValue))
                return expiringValue.Value;

            await expiringValue.WriterSemaphore.WaitAsync();
            try
            {
                var currentValue = _cache.GetOrAdd(key, k => new WriteableCacheValue<TValue>(null, DateTimeOffset.MinValue));
                if (currentValue.Value == null ||
                    _invalidationStrategy.ShouldInvalidate(currentValue))
                {
                    Logger.DebugFormat("Value for key {key} is stale, refreshing..", key);
                    Set(key, await _valueFactory.Get(key));
                }
            }
            catch (Exception ex)
            {
                Logger.ErrorException($"Exception while refreshing value for key {key}", ex);
                if (!AllowStale || _cache.TryGet(key)?.Value == null)
                {
                    Remove(key);
                }
            }
            finally
            {
                expiringValue.WriterSemaphore.Release();
            }

            return _cache.TryGet(key)?.Value;
        }


        public virtual void Set(TKey key, TValue value)
        {
            var expiringValue = new WriteableCacheValue<TValue>(value, _timeProvider());
            _cache.AddOrUpdate(key, expiringValue, (_, __) => expiringValue);
        }

        public virtual void Remove(TKey key)
        {
            _cache.TryRemove(key, out _);
        }
    }

    internal class WriteableCacheValue<TValue> : CacheValue<TValue>
    {
        public WriteableCacheValue(TValue value, DateTimeOffset created)
            : base(value, created)
        {
            WriterSemaphore = new SemaphoreSlim(1, 1);
        }

        public SemaphoreSlim WriterSemaphore { get; }
    }
}