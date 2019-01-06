using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
            : this(valueFactory, invalidationStrategy, TimeProviders.Default)
        {
        }

        internal DotCache(IValueFactory<TKey, TValue> valueFactory, IInvalidationStrategy<TValue> invalidationStrategy,
            TimeProvider timeProvider)
        {
            _valueFactory = valueFactory;
            _invalidationStrategy = invalidationStrategy;
            _timeProvider = timeProvider;
        }

        public async Task<TValue> GetOrNull(TKey key)
        {
            var expiringValue =
                _cache.GetOrAdd(key, k => new WriteableCacheValue<TValue>(null, DateTimeOffset.MinValue));
            if (expiringValue.Value != null && !_invalidationStrategy.ShouldInvalidate(expiringValue))
                return expiringValue.Value;

            await expiringValue.WriterSemaphore.WaitAsync();
            try
            {
                var currentValue = _cache[key];
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
                Set(key, null);
            }
            finally
            {
                expiringValue.WriterSemaphore.Release();
            }

            return _cache[key].Value;
        }

        public void Set(TKey key, TValue value)
        {
            var expiringValue = new WriteableCacheValue<TValue>(value, _timeProvider());
            _cache.AddOrUpdate(key, expiringValue, (_, __) => expiringValue);
        }
    }

    public class CacheValue<TValue>
    {
        public CacheValue(TValue value, DateTimeOffset created)
        {
            Value = value;
            Created = created;
        }

        public TValue Value { get; }
        public DateTimeOffset Created { get; }
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

    public interface IInvalidationStrategy<TValue>
    {
        bool ShouldInvalidate(CacheValue<TValue> expiringValue);
    }
}