using System;

namespace DotCached.Core
{
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
}