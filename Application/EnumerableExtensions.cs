using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NugetMirror.Application
{
    public static class EnumerableExtensions
    {
        public static async Task ForEachInParallelAsync<T>(this IAsyncEnumerable<T> enumerable, int degreeOfParallelism, Func<T, Task> action)
        {
            var throttler = new SemaphoreSlim(degreeOfParallelism);

            var tasks = new List<Task>();
            await foreach (var item in enumerable)
            {
                tasks.Add(RunTask(item));
            }

            await Task.WhenAll(tasks);

            async Task RunTask(T item)
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
            }
        }
    }
}
