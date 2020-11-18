using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Common;
using NuGet.Protocol.Core.Types;
using NugetMirror.Application.Mirror;
using Serilog;
using Spectre.Cli;
using Spectre.Cli.Extensions.DependencyInjection;
using ILogger = NuGet.Common.ILogger;

namespace NugetMirror.Application
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateLogger();

            var services = new ServiceCollection();

            services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));

            services.AddSingleton(provider => new SourceCacheContext { NoCache = true });
            services.AddTransient<ILogger, Logger>(provider => new Logger(LogLevel.Minimal));

            using var registrar = new DependencyInjectionRegistrar(services);
            var app = new CommandApp(registrar);
            app.Configure(config =>
            {
                config.AddCommand<MirrorCommand>("mirror");
            });

            return await app.RunAsync(args);
        }
    }
}
