namespace Aegis.Cafeteria.Infrastructure.Logging;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

internal sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly CafeteriaLoggingOptions options;
    private readonly CafeteriaLogContext context;
    private readonly Channel<string> queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource stop = new();
    private readonly Task writerTask;
    private readonly ConcurrentDictionary<string, ILogger> loggers = new(StringComparer.Ordinal);

    public RollingFileLoggerProvider(CafeteriaLoggingOptions options, CafeteriaLogContext context)
    {
        this.options = options;
        this.context = context;
        writerTask = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) =>
        loggers.GetOrAdd(categoryName, name => new ProviderLogger(name, Enqueue));

    private void Enqueue(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] " +
            $"{context.ApplicationName} {context.TerminalId} {context.MachineName} {category} ({eventId.Id}) {message}" +
            (exception is null ? string.Empty : Environment.NewLine + exception);
        queue.Writer.TryWrite(line);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var line in queue.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                try
                {
                    var path = GetCurrentPath();
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);
                    await File.AppendAllTextAsync(path, line + Environment.NewLine, stop.Token).ConfigureAwait(false);
                    CleanupOldFiles(path);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch
                {
                    // Logging must never crash the POS.
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private string GetCurrentPath()
    {
        var configured = options.LocalLogPath;
        var directory = Path.GetDirectoryName(configured) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(configured).TrimEnd('-');
        var ext = Path.GetExtension(configured);
        return Path.Combine(directory, $"{stem}-{DateTime.Now:yyyyMMdd}{ext}");
    }

    private void CleanupOldFiles(string currentPath)
    {
        if (options.RetainedFileCountLimit <= 0) return;
        var directory = Path.GetDirectoryName(currentPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        var stem = Path.GetFileNameWithoutExtension(options.LocalLogPath).TrimEnd('-');
        var ext = Path.GetExtension(options.LocalLogPath);
        foreach (var file in Directory.EnumerateFiles(directory, $"{stem}-*{ext}")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(options.RetainedFileCountLimit))
        {
            try { File.Delete(file); } catch { }
        }
    }

    public void Dispose()
    {
        queue.Writer.TryComplete();
        stop.CancelAfter(TimeSpan.FromSeconds(2));
        try { writerTask.GetAwaiter().GetResult(); } catch { }
        stop.Dispose();
    }

    private sealed class ProviderLogger(string category, Action<LogLevel, string, EventId, string, Exception?> write) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            write(logLevel, category, eventId, formatter(state, exception), exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
