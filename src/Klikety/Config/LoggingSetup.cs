using System.IO;

using Microsoft.Extensions.Logging;

namespace Klikety.Config;

/// <summary>
/// Builds the application ILoggerFactory with a rolling file sink
/// writing to %APPDATA%\Klikety\logs\.
/// </summary>
public static class LoggingSetup {
    private static readonly string LogFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety", "logs");

    public static ILoggerFactory CreateLoggerFactory(string logLevelName, bool fileLoggingEnabled, int retainedFileCount) {
        if (!Enum.TryParse<LogLevel>(logLevelName, true, out var logLevel)) {
            logLevel = LogLevel.Warning;
        }

        return LoggerFactory.Create(builder => {
            builder.SetMinimumLevel(logLevel);

            if (fileLoggingEnabled) {
                Directory.CreateDirectory(LogFolder);
                var logPath = Path.Combine(LogFolder, "klikety-{Date}.log");
                builder.AddFile(logPath, logLevel, retainedFileCountLimit: retainedFileCount);
            }
        });
    }
}
