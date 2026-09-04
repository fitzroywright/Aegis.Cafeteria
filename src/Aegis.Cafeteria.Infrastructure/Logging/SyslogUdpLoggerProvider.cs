namespace Aegis.Cafeteria.Infrastructure.Logging;

using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

internal sealed class SyslogUdpLoggerProvider : ILoggerProvider
{
    private readonly string host;
    private readonly int port;
    private readonly CafeteriaLogContext context;
    private readonly UdpClient client = new();

    public SyslogUdpLoggerProvider(string host, int port, CafeteriaLogContext context)
    {
        this.host = host;
        this.port = port;
        this.context = context;
    }

    public ILogger CreateLogger(string categoryName) => new SyslogUdpLogger(this.client, this.host, this.port, this.context, categoryName);

    public void Dispose() => this.client.Dispose();

    private sealed class SyslogUdpLogger : ILogger
    {
        private readonly UdpClient client;
        private readonly string host;
        private readonly int port;
        private readonly CafeteriaLogContext context;
        private readonly string category;

        public SyslogUdpLogger(UdpClient client, string host, int port, CafeteriaLogContext context, string category)
        {
            this.client = client;
            this.host = host;
            this.port = port;
            this.context = context;
            this.category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            try
            {
                int severity = logLevel switch
                {
                    LogLevel.Critical => 2,
                    LogLevel.Error => 3,
                    LogLevel.Warning => 4,
                    LogLevel.Information => 6,
                    LogLevel.Debug => 7,
                    LogLevel.Trace => 7,
                    _ => 6
                };
                int priority = 16 * 8 + severity;
                string message = formatter(state, exception).Replace('\r', ' ').Replace('\n', ' ');
                if (exception is not null)
                    message += " | " + exception;

                string payload = $"<{priority}>1 {DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {this.context.MachineName} {this.context.ApplicationName} - {eventId.Id} [aegis terminal=\"{Escape(this.context.TerminalId)}\" category=\"{Escape(this.category)}\"] {message}";
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                _ = this.client.SendAsync(bytes, bytes.Length, this.host, this.port);
            }
            catch
            {
            }
        }

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("]", "\\]");
    }
}
