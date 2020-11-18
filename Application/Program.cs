using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Application
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddTransient<ListPackagesCommand>();

            using var scope = serviceCollection.BuildServiceProvider().CreateScope();
            var listPackagesCommand = scope.ServiceProvider.GetRequiredService<ListPackagesCommand>();
            await listPackagesCommand.Execute(CancellationToken.None);
        }
    }
}
