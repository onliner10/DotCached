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
    public interface IValueProvider<TKey, TValue>
        where TValue : class
    {
        Task<TValue> GetOrNullAsync(TKey key);
    }

    public interface IInternalCache<TKey, TValue> 
        where TValue : class
    {
        WriteableCacheValue<TValue> GetOrInit(TKey key);
        WriteableCacheValue<TValue> GetOrNull(TKey key);
        void Set(TKey key, TValue value);
        void Remove(TKey key);
        bool Contains(TKey key);
    }

    public class InMemoryCache<TKey, TValue> : IInternalCache<TKey, TValue>
        where TValue : class
    {
        private readonly ConcurrentDictionary<TKey, WriteableCacheValue<TValue>> _cache =
            new ConcurrentDictionary<TKey, WriteableCacheValue<TValue>>();
        private readonly TimeProvider _timeProvider;

        public InMemoryCache(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public WriteableCacheValue<TValue> GetOrNull(TKey key)
        {
            return _cache.TryGetValue(key, out var value) ? value : null;
        }

        public void Set(TKey key, TValue value)
        {
            var newValue = new WriteableCacheValue<TValue>(value, _timeProvider());
            _cache.AddOrUpdate(key, newValue, (_, __) => newValue);
        }

        public void Remove(TKey key) => _cache.TryRemove(key, out _);

        public bool Contains(TKey key) => _cache.ContainsKey(key);

        public WriteableCacheValue<TValue> GetOrInit(TKey key) => _cache.GetOrAdd(key, new WriteableCacheValue<TValue>(null, _timeProvider()));
    }

    public class InvalidatingCache<TKey, TValue> : IInternalCache<TKey, TValue>
        where TValue : class
    {
        private readonly IInternalCache<TKey, TValue> _inner;
        private readonly IInvalidationStrategy<TValue> _invalidationStrategy;

        public InvalidatingCache(IInternalCache<TKey, TValue> inner, IInvalidationStrategy<TValue> invalidationStrategy)
        {
            _inner = inner;
            _invalidationStrategy = invalidationStrategy;
        }

        public bool Contains(TKey key) => _inner.Contains(key);

        public WriteableCacheValue<TValue> GetOrInit(TKey key) => _inner.GetOrInit(key);

        public WriteableCacheValue<TValue> GetOrNull(TKey key)
        {
            var innerValue = _inner.GetOrNull(key);

            if (innerValue == null || !_invalidationStrategy.ShouldInvalidate(innerValue))
                return innerValue;

            _inner.Remove(key);
            return _inner.GetOrNull(key);
        }

        public void Remove(TKey key) => _inner.Remove(key);

        public void Set(TKey key, TValue value) => _inner.Set(key, value);
    }

    public class FifoSizeLimitedCache<TKey, TValue> : IInternalCache<TKey, TValue>
        where TValue : class
    {
        private ConcurrentQueue<TKey> _fifoKeys = new ConcurrentQueue<TKey>();
        private readonly IInternalCache<TKey, TValue> _inner;

        public int MaxSize { get; }
        private int _currentSize = 0;

        internal FifoSizeLimitedCache(int maxSize, IInternalCache<TKey, TValue> inner)
        {
            MaxSize = maxSize;
            _inner = inner;
        }

        public WriteableCacheValue<TValue> GetOrNull(TKey key) => _inner.GetOrNull(key);

        public WriteableCacheValue<TValue> GetOrInit(TKey key)
        {
            var value = GetOrNull(key);
            if (value.Value != null) return value;

            value.WriterSemaphore.Wait();
            try
            {
                var currentValue = GetOrNull(key);
                if (currentValue != null) return currentValue;

                Set(key, null);
                return GetOrNull(key);
            }
            finally
            {
                value.WriterSemaphore.Release();
            }
        }

        public void Remove(TKey key)
        {
            Interlocked.Decrement(ref _currentSize);
            _inner.Remove(key);
        }

        public void Set(TKey key, TValue value)
        {
            if(!_inner.Contains(key))
            {
                Interlocked.Increment(ref _currentSize);
                var oversize = _currentSize - MaxSize;
                while(oversize > 0)
                {

                    if(_fifoKeys.TryDequeue(out var keyToRemove))
                    {
                        _inner.Remove(keyToRemove);
                    }else
                    {
                        break;
                    }
                    oversize = _currentSize - MaxSize;
                }
            }

            _inner.Set(key, value);
        }

        public bool Contains(TKey key) => _inner.Contains(key);
    }


    public class LazyValueProvider<TKey, TValue> : IValueProvider<TKey, TValue>
        where TValue : class
    {
        private static readonly ILog Logger = LogProvider.For<LazyValueProvider<TKey, TValue>>();

        private readonly TimeProvider _timeProvider;

        private readonly IValueFactory<TKey, TValue> _valueFactory;
        private readonly IInternalCache<TKey, TValue> _cache;

        public LazyValueProvider(IValueFactory<TKey, TValue> valueFactory, IInternalCache<TKey, TValue> cache)
            : this(valueFactory,  cache, true, TimeProviders.Default)
        {
        }

        internal LazyValueProvider(
            IValueFactory<TKey, TValue> valueFactory,
            IInternalCache<TKey, TValue> cache,
            bool allowStale,
            TimeProvider timeProvider)
        {
            AllowStale = allowStale;
            _valueFactory = valueFactory;
            _cache = cache;
            _timeProvider = timeProvider;
        }

        public bool AllowStale { get; }

        public virtual async Task<TValue> GetOrNullAsync(TKey key)
        {
            var expiringValue = _cache.GetOrInit(key);
            if (expiringValue.Value != null)
                return expiringValue.Value;

            await expiringValue.WriterSemaphore.WaitAsync();
            try
            {
                var currentValue = _cache.GetOrNull(key);
                if (currentValue.Value == null)
                {
                    Logger.DebugFormat("Value for key {key} is stale, refreshing..", key);
                    _cache.Set(key, await _valueFactory.Get(key));
                }
            }
            catch (Exception ex)
            {
                Logger.ErrorException($"Exception while refreshing value for key {key}", ex);
                if (!AllowStale || _cache.GetOrNull(key)?.Value == null)
                {
                    _cache.Remove(key);
                }
            }
            finally
            {
                expiringValue.WriterSemaphore.Release();
            }

            return _cache.GetOrNull(key)?.Value;
        }
    }

    public class WriteableCacheValue<TValue> : CacheValue<TValue>
    {
        public WriteableCacheValue(TValue value, DateTimeOffset created)
            : base(value, created)
        {
            WriterSemaphore = new SemaphoreSlim(1, 1);
        }

        public SemaphoreSlim WriterSemaphore { get; }
    }
}