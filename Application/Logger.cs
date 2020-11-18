using System;
using System.Threading.Tasks;
using NuGet.Common;
using Serilog.Events;

namespace NugetMirror.Application
{
    public class Logger : LoggerBase
    {
        private readonly LogLevel minimumLevel;

        public Logger(LogLevel minimumLevel)
        {
            this.minimumLevel = minimumLevel;
        }

        public override void Log(ILogMessage message)
        {
            if (message.Level < minimumLevel)
            {
                return;
            }

            var logEventLevel = message.Level switch
            {
                LogLevel.Verbose => LogEventLevel.Verbose,
                LogLevel.Debug => LogEventLevel.Debug,
                LogLevel.Information => LogEventLevel.Information,
                LogLevel.Minimal => LogEventLevel.Information,
                LogLevel.Warning => LogEventLevel.Warning,
                LogLevel.Error => LogEventLevel.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(message))
            };

            Serilog.Log
                .ForContext("Code", message.Code)
                .ForContext("ProjectPath", message.ProjectPath)
                .Write(logEventLevel, "{Message}", message.Message);
        }

        public override Task LogAsync(ILogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }
    }
}
