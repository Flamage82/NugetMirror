using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NugetMirror.Application
{
    public static class EnumerableExtensions
    {
        public static async Task ForEachInParallelAsync<T>(this IEnumerable<T> enumerable, int degreeOfParallelism, Func<T, Task> action)
        {
            var throttler = new SemaphoreSlim(degreeOfParallelism);
            var tasks = enumerable.Select(async item =>
            {
                try
                {
                    await throttler.WaitAsync();
                    await action(item);
                }
                finally
                {
                    throttler.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
    }
}