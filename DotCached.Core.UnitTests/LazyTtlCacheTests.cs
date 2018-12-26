using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Castle.Components.DictionaryAdapter;
using FluentAssertions;
using Moq;
using Xunit;

namespace DotCached.Core.UnitTests
{
    public class LazyTtlCacheTests
    {
        private readonly Mock<IValueFactory<string, string>> _dummyValueFactory;
        private const string DummyKey = "DummyKey";

        public LazyTtlCacheTests()
        {
            var dummyValueFactory = new Mock<IValueFactory<string, string>>();
            dummyValueFactory.Setup(x => x.Get(DummyKey)).ReturnsAsync(DummyKey);
            _dummyValueFactory = dummyValueFactory;
        }

        [Fact]
        public async Task GetOrNull_KeyNotPresentInCache_ValueFactoryGetsCalled()
        {
            var cache = new LazyTtlCache<string, string>(TimeSpan.FromMinutes(1), _dummyValueFactory.Object);

            var result = await cache.GetOrNull(DummyKey);
            result.Should().Be(DummyKey);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Once);
            
        }
        
        [Fact]
        public async Task GetOrNull_ValueHasExpired_NewValueIsCreated()
        {
            var cache = new LazyTtlCache<string, string>(TimeSpan.FromMinutes(1), _dummyValueFactory.Object);

            var result = await cache.GetOrNull(DummyKey);
            result.Should().Be(DummyKey);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Once);
        }

        public static IEnumerable<object[]> ThreadCounts => Enumerable.Range(1, 30).Cast<object>().Select(x=> new[] {x});
       
        [Theory]
        [MemberData(nameof(ThreadCounts))]
        public void GetOrNull_MultipleThreads_ValueFactoryIsCalledExatlyOnce(int threadCount)
        {
            var cache = new LazyTtlCache<string, string>(TimeSpan.FromMinutes(1), _dummyValueFactory.Object);
            
            var results = StartThreads(threadCount, () => cache.GetOrNull(DummyKey).Result);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Once);
        }
        
        [Theory]
        [MemberData(nameof(ThreadCounts))]
        public void GetOrNull_MultipleThreadsAndValueDoesNotExist_AllThreadsReturnRefreshedValue(int threadCount)
        {
            var cache = new LazyTtlCache<string, string>(TimeSpan.FromMinutes(1), _dummyValueFactory.Object);
            var value = Guid.NewGuid().ToString();
            _dummyValueFactory
                .Setup(x => x.Get(DummyKey))
                .ReturnsAsync(value);

            var results = StartThreads(threadCount, () => cache.GetOrNull(DummyKey).Result);

            results.Count().Should().Be(threadCount);
            results.Distinct().Should().BeEquivalentTo(value);
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
            foreach (var thread in threads)
            {
                thread.Join();
            }

            return results;
        }
    }
}