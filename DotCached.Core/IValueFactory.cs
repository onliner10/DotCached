using System.Threading.Tasks;

namespace DotCached.Core
{
    public interface IValueFactory<TKey, TValue>
    {
        Task<TValue> Get(TKey key);
    }
}