namespace DotCached.Core
{
    public interface IValueFactory<TKey, TValue>
    {
        TValue Get(TKey key);
    }
}