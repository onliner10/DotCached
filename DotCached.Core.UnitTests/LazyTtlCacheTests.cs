using System;
using FluentAssertions;
using Moq;
using Xunit;

namespace DotCached.Core.UnitTests
{
    public class LazyTtlCacheTests
    {
        private readonly Mock<IValueFactory<string, string>> _dummyValueFactory;
        private const string DummyKey = "1";

        public LazyTtlCacheTests()
        {
            var dummyValueFactory = new Mock<IValueFactory<string, string>>();
            dummyValueFactory.Setup(x => x.Get(DummyKey)).Returns(DummyKey);
            _dummyValueFactory = dummyValueFactory;
        }

        [Fact]
        public void WhenRequestingItem_WhichIsNotPresent_ItShouldBeCreatedAndReturned()
        {
            var cache = new LazyTtlCache<string, string>(TimeSpan.MaxValue, _dummyValueFactory.Object);

            cache.GetOrNull(DummyKey).Should().Be(DummyKey);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Once);
            
        }
        
        [Fact]
        public void SquareBrackets_ShouldReturnSameInstanceAs_GetOrNull()
        {
            var cache = new LazyTtlCache<string, string>(TimeSpan.MaxValue, _dummyValueFactory.Object);

            cache.GetOrNull(DummyKey).Should().BeSameAs(cache[DummyKey]);
        }
        
        [Fact]
        public void WhenItemHasExpired_NewValueShouldBeCreated_AndReturned()
        {
            var cache = new LazyTtlCache<string, string>(TimeSpan.FromMinutes(1), _dummyValueFactory.Object);

            cache.GetOrNull(DummyKey).Should().Be(DummyKey);
            _dummyValueFactory.Verify(vf => vf.Get(DummyKey), Times.Once);
            
        }
    }
}