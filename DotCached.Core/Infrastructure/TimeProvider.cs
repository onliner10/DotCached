using System;

namespace DotCached.Core.Infrastructure
{
    public delegate DateTimeOffset TimeProvider();

    public static class TimeProviders
    {
        public static TimeProvider Default = () => DateTimeOffset.Now;
    }
}