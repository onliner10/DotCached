namespace DotCached.Core
{
    public interface IInvalidationStrategy<TValue>
    {
        bool ShouldInvalidate(CacheValue<TValue> expiringValue);
    }
}