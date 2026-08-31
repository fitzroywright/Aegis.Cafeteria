namespace Aegis.Cafeteria.Infrastructure.Logging;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

internal sealed class GelfUdpLoggerProvider : ILoggerProvider
{
    private readonly CafeteriaLoggingOptions options;
    private readonly CafeteriaLogContext context;
    private readonly Channel<byte[]> queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2048)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly CancellationTokenSource stop = new();
    private readonly Task senderTask;
    private readonly ConcurrentDictionary<string, ILogger> loggers = new(StringComparer.Ordinal);

    public GelfUdpLoggerProvider(CafeteriaLoggingOptions options, CafeteriaLogContext context)
    {
        this.options = options;
        this.context = context;
        senderTask = Task.Run(SendLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) =>
        loggers.GetOrAdd(categoryName, name => new ProviderLogger(name, Enqueue));

    private void Enqueue(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        // Never include configuration objects, connection strings, identifiers, access tokens or secret payloads here.
        var payload = new Dictionary<string, object?>
        {
            ["version"] = "1.1",
            ["host"] = context.MachineName,
            ["short_message"] = message,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d,
            ["level"] = ToSyslogLevel(level),
            ["_application"] = context.ApplicationName,
            ["_terminal_id"] = context.TerminalId,
            ["_category"] = category,
            ["_event_id"] = eventId.Id
        };
        if (exception is not null)
        {
            payload["full_message"] = exception.ToString();
            payload["_exception_type"] = exception.GetType().FullName;
        }
        queue.Writer.TryWrite(JsonSerializer.SerializeToUtf8Bytes(payload));
    }

    private async Task SendLoopAsync()
    {
        try
        {
            using var udp = new UdpClient();
            var addresses = await Dns.GetHostAddressesAsync(options.GraylogHost, stop.Token).ConfigureAwait(false);
            var address = addresses.FirstOrDefault() ?? IPAddress.Parse(options.GraylogHost);
            var endpoint = new IPEndPoint(address, options.GraylogPort);
            await foreach (var payload in queue.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                try { await udp.SendAsync(payload, endpoint, stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch { /* Graylog failure must never affect the POS. */ }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch { }
    }

    private static int ToSyslogLevel(LogLevel level) => level switch
    {
        LogLevel.Critical => 2,
        LogLevel.Error => 3,
        LogLevel.Warning => 4,
        LogLevel.Information => 6,
        LogLevel.Debug or LogLevel.Trace => 7,
        _ => 6
    };

    public void Dispose()
    {
        queue.Writer.TryComplete();
        stop.CancelAfter(TimeSpan.FromSeconds(2));
        try { senderTask.GetAwaiter().GetResult(); } catch { }
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
