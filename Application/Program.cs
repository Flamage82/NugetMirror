using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
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
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .CreateLogger();

            try
            {
                var services = new ServiceCollection();

                services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));

                services.AddSingleton(_ => new SourceCacheContext { NoCache = true });
                services.AddTransient<ILogger, Logger>(_ => new Logger(LogLevel.Minimal));

                using var registrar = new DependencyInjectionRegistrar(services);
                var app = new CommandApp(registrar);
                app.Configure(config =>
                {
                    config.AddCommand<MirrorCommand>("mirror");
                });

                return await app.RunAsync(args);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
