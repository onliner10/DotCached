using System;
using System.Threading.Tasks;
using DotCached.Core;

namespace DotCached.Examples
{
    internal class Program
    {
        private static void Main(string[] args)
        {
//            var cache = new DotCache<string, string>(new SimpleValueFactory());
            Console.WriteLine("Hello World!");
        }
    }

    public class SimpleValueFactory : IValueFactory<string, string>
    {
        public Task<string> Get(string key)
        {
            return Task.FromResult(key);
        }
    }

//    public class TtlInvalidation : IInvalidationStrategy<string>
//    {
//        public bool ShouldInvalidate(CacheValue<string> expiringValue)
//        {
//            expiringValue.Created.
//        }
//    }
}