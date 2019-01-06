using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotCached.Core.Infrastructure;
using FluentAssertions;
using Moq;
using Xunit;

namespace DotCached.Core.UnitTests
{
    public class LazyTtlCacheTests
    {
        private const string DummyKey = "DummyKey";

        private readonly Mock<IValueFactory<string, string>> _dummyValueFactory;

        public LazyTtlCacheTests()
        {
            var dummyValueFactory = new Mock<IValueFactory<string, string>>();
            dummyValueFactory.Setup(x => x.Get(DummyKey)).ReturnsAsync(DummyKey);
            _dummyValueFactory = dummyValueFactory;
        }

        public static IEnumerable<object[]> ThreadCounts =>
            Enumerable.Range(1, 30).Cast<object>().Select(x => new[] {x});

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
            var timeProvider = CreateIncrementalTimeProvider();

            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(false);

            var cache = new DotCache<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object,
                timeProvider);

            var results = StartThreads(threadCount, () => cache.GetOrNull(DummyKey).Result);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Once);
        }

        [Theory]
        [MemberData(nameof(ThreadCounts))]
        public void GetOrNull_MultipleThreadsAndValueDoesNotExist_AllThreadsReturnRefreshedValue(int threadCount)
        {
            var timeProvider = CreateIncrementalTimeProvider();
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(false);

            var cache = new DotCache<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object,
                timeProvider);
            var value = Guid.NewGuid().ToString();
            _dummyValueFactory
                .Setup(x => x.Get(DummyKey))
                .ReturnsAsync(value);

            var results = StartThreads(threadCount, () => cache.GetOrNull(DummyKey).Result);

            results.Count().Should().Be(threadCount);
            results.Distinct().Should().BeEquivalentTo(value);
        }

        [Theory]
        [MemberData(nameof(ThreadCounts))]
        public void GetOrNull_ValueFactoryThrowsException_AllThreadsReturnNull_1(int threadCount)
        {
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(true);

            var cache = new DotCache<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);
            _dummyValueFactory
                .Setup(x => x.Get(DummyKey))
                .ThrowsAsync(new Exception());

            var results = StartThreads(threadCount, () => cache.GetOrNull(DummyKey).Result);

            results.Count().Should().Be(threadCount);
            results.Distinct().Should().BeEquivalentTo(new string[] {null});
        }

        [Theory]
        [MemberData(nameof(ThreadCounts))]
        public void GetOrNull_ValueFactoryThrowsException_AllThreadsReturnNull_2(int threadCount)
        {
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(true);

            var cache = new DotCache<string, string>(_dummyValueFactory.Object,
                invalidationStrategyMock.Object);
            _dummyValueFactory
                .SetupSequence(x => x.Get(DummyKey))
                .ReturnsAsync(DummyKey)
                .ThrowsAsync(new Exception());

            StartThreads(threadCount, () => cache.GetOrNull(DummyKey).Result);
            var results = StartThreads(threadCount, () => cache.GetOrNull(DummyKey).Result);

            results.Count().Should().Be(threadCount);
            results.Distinct().Should().BeEquivalentTo(new string[] {null});
        }
        
        [Fact]
        public async Task GetOrNull_KeyNotPresentInCache_ValueFactoryGetsCalled()
        {  
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            var cache = new DotCache<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);

            var result = await cache.GetOrNull(DummyKey);
            result.Should().Be(DummyKey);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Once);
        }

        [Fact]
        public async Task GetOrNull_ValueHasExpired_NewValueIsCreated()
        {  
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(true);

            var cache = new DotCache<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);
           
            // to populate value
            await cache.GetOrNull(DummyKey);
            
            // simulate it's stale by now
            var result = await cache.GetOrNull(DummyKey);
            
            result.Should().Be(DummyKey);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Exactly(2));
        }
        
        [Fact]
        public async Task GetOrNull_ValueNotExpired_FactoryIsNotCalled()
        {  
            var invalidationStrategyMock = new Mock<IInvalidationStrategy<string>>();
            invalidationStrategyMock
                .Setup(x => x.ShouldInvalidate(It.IsAny<CacheValue<string>>())).Returns(false);

            var cache = new DotCache<string, string>(_dummyValueFactory.Object, invalidationStrategyMock.Object);
           
            // to populate value
            await cache.GetOrNull(DummyKey);
            
            // simulate it's stale by now
            var result = await cache.GetOrNull(DummyKey);
            
            result.Should().Be(DummyKey);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Once);
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
    }
}