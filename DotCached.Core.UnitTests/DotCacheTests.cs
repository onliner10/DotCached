using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Schema;
using DotCached.Core.Infrastructure;
using FluentAssertions;
using Moq;
using Xunit;

namespace DotCached.Core.UnitTests
{
    public class LazyTtlCacheTests
    {
        public LazyTtlCacheTests()
        {
            var dummyValueFactory = new Mock<IValueFactory<string, string>>();
            dummyValueFactory.Setup(x => x.Get(DummyKey)).ReturnsAsync(DummyKey);
            _dummyValueFactory = dummyValueFactory;
        }

        private const string DummyKey = "DummyKey";

        private readonly Mock<IValueFactory<string, string>> _dummyValueFactory;

        public static IEnumerable<object[]> ThreadCounts =>
            Enumerable.Range(1, 30).Cast<object>().Select(x => new[] {x});

        public static IEnumerable<object[]> ThreadCountsWithBool =>
            Enumerable.Range(1, 30).Cast<object>().SelectMany(x => new[] {new[] {x, true}, new[] {x, false}});

        
        private TimeProvider CreateIncrementalTimeProvider()
        {
            var callCounter = 0;

            DateTimeOffset DummyProvider()
            {
                var res = DateTimeOffset.UnixEpoch.Add(callCounter * TimeSpan.FromMinutes(1));
                Interlocked.Increment(ref callCounter);
                return res;
            }

            return DummyProvider;
        }

        [Theory]
        [MemberData(nameof(ThreadCounts))]
        public void GetOrNull_MultipleThreads_ValueFactoryIsCalledExatlyOnce(int threadCount)
        {
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(false);

            var cache = new LazyValueProvider<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);

            var results = StartThreads(threadCount, () => cache.GetOrNullAsync(DummyKey).Result);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Once);
        }

        [Theory]
        [MemberData(nameof(ThreadCounts))]
        public void GetOrNull_MultipleThreadsAndValueDoesNotExist_AllThreadsReturnRefreshedValue(int threadCount)
        {
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(false);

            var cache = new LazyValueProvider<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);
            var value = Guid.NewGuid().ToString();
            _dummyValueFactory
                .Setup(x => x.Get(DummyKey))
                .ReturnsAsync(value);

            var results = StartThreads(threadCount, () => cache.GetOrNullAsync(DummyKey).Result);

            results.Count().Should().Be(threadCount);
            results.Distinct().Should().BeEquivalentTo(value);
        }

        [Theory]
        [MemberData(nameof(ThreadCounts))]
        public void GetOrNull_ValueFactoryThrowsException_ItRespectsAllowStale_1(int threadCount)
        {
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(true);

            var cache = new LazyValueProvider<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);
            _dummyValueFactory
                .Setup(x => x.Get(DummyKey))
                .ThrowsAsync(new Exception());

            var results = StartThreads(threadCount, () => cache.GetOrNullAsync(DummyKey).Result);

            results.Count().Should().Be(threadCount);
            
            results.Distinct().Should().BeEquivalentTo(new string[] {null});
            cache.Count.Should().Be(0);
        }

        [Theory]
        [MemberData(nameof(ThreadCountsWithBool))]
        public void GetOrNull_ValueFactoryThrowsException_AllThreadsReturnNull_2(int threadCount, bool allowStale)
        {
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(true);

            var cache = new LazyValueProvider<string, string>(_dummyValueFactory.Object,invalidationStrategyMock.Object, allowStale, TimeProviders.Default);
            _dummyValueFactory
                .SetupSequence(x => x.Get(DummyKey))
                .ReturnsAsync(DummyKey)
                .ThrowsAsync(new Exception());

            StartThreads(threadCount, () => cache.GetOrNullAsync(DummyKey).Result);
            var results = StartThreads(threadCount, () => cache.GetOrNullAsync(DummyKey).Result);

            results.Count().Should().Be(threadCount);

            if (allowStale)
            {
                results.Distinct().Should().BeEquivalentTo(DummyKey);
            }
            else
            {
                results.Distinct().Should().BeEquivalentTo(new string[] {null});
            }
        }

        private static ConcurrentBag<TOut> StartThreads<TOut>(
            int threadCount,
            Func<TOut> action)
        {
            var resetEvent = new ManualResetEventSlim();
            var results = new ConcurrentBag<TOut>();

            var threads = Enumerable.Range(0, threadCount).Select(i =>
            {
                var t = new Thread(() =>
                {
                    resetEvent.Wait();
                    results.Add(action());
                }) {Name = $"Test Thread No. {i}"};
                t.Start();
                return t;
            }).ToList();

            resetEvent.Set();
            foreach (var thread in threads) thread.Join();

            return results;
        }

        [Fact]
        public async Task GetOrNull_KeyNotPresentInCache_ValueFactoryGetsCalled()
        {
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            var cache = new LazyValueProvider<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);

            var result = await cache.GetOrNullAsync(DummyKey);
            result.Should().Be(DummyKey);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Once);
        }

        [Fact]
        public async Task GetOrNull_ValueAlreadySet_ValueIsRetrieved()
        {
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(false);

            var cache = new LazyValueProvider<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);
            cache.Set(DummyKey, DummyKey);

            cache.Count.Should().Be(1);
            (await cache.GetOrNullAsync(DummyKey)).Should().Be(DummyKey);
        }

        [Fact]
        public async Task GetOrNull_ValueHasExpired_NewValueIsCreated()
        {
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(true);

            var cache = new LazyValueProvider<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);

            cache.Set(DummyKey, DummyKey);

            var result = await cache.GetOrNullAsync(DummyKey);

            result.Should().Be(DummyKey);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Once);
        }

        [Fact]
        public async Task GetOrNull_ValueNotExpired_FactoryIsNotCalled()
        {
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(false);

            var cache = new LazyValueProvider<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);

            cache.Set(DummyKey, DummyKey);

            // simulate it's stale by now
            var result = await cache.GetOrNullAsync(DummyKey);

            result.Should().Be(DummyKey);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Never);
        }

        [Fact]
        public void GetOrNull_ValueSetAndRemoved_NullIsReturned()
        {
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(false);

            var cache = new LazyValueProvider<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);
            cache.Set(DummyKey, DummyKey);
            cache.Remove(DummyKey);

            cache.Count.Should().Be(0);
        }
    }
}