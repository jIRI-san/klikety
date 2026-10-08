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

    public static ILoggerFactory CreateLoggerFactory(
        string logLevelName, bool fileLoggingEnabled, int retainedFileCount, string? logFolder = null) {
        if (!Enum.TryParse<LogLevel>(logLevelName, true, out var logLevel)) {
            logLevel = LogLevel.Warning;
        }

        var targetLogFolder = logFolder ?? LogFolder;
        return LoggerFactory.Create(builder => {
            builder.SetMinimumLevel(logLevel);

            if (fileLoggingEnabled) {
                Directory.CreateDirectory(targetLogFolder);
                var logPath = Path.Combine(targetLogFolder, "klikety-{Date}.log");
                builder.AddFile(logPath, logLevel, retainedFileCountLimit: retainedFileCount);
            }
        });
    }
}
