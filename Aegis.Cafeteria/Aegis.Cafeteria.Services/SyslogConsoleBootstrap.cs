namespace Aegis.Cafeteria.Services;

using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

internal static class SyslogConsoleBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Console.SetOut(new SyslogForwardingTextWriter(Console.Out, "Aegis.Cafeteria.Services"));
        Console.SetError(new SyslogForwardingTextWriter(Console.Error, "Aegis.Cafeteria.Services"));
    }

    private sealed class SyslogForwardingTextWriter : TextWriter
    {
        private const string Host = "10.10.0.3";
        private const int Port = 1699;
        private readonly TextWriter inner;
        private readonly string applicationName;
        private readonly StringBuilder buffer = new();
        private readonly object sync = new();
        private readonly UdpClient client = new();

        public SyslogForwardingTextWriter(TextWriter inner, string applicationName)
        {
            this.inner = inner;
            this.applicationName = applicationName;
        }

        public override Encoding Encoding => this.inner.Encoding;

        public override void Write(char value)
        {
            this.inner.Write(value);
            lock (this.sync)
            {
                if (value == '\n')
                    FlushLine();
                else if (value != '\r')
                    this.buffer.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value is null)
                return;
            foreach (char character in value)
                Write(character);
        }

        public override void WriteLine(string? value)
        {
            if (value is not null)
                Write(value);
            Write('\n');
        }

        private void FlushLine()
        {
            if (this.buffer.Length == 0)
                return;

            string line = this.buffer.ToString();
            this.buffer.Clear();
            try
            {
                string payload = $"<134>1 {DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {Environment.MachineName} {this.applicationName} - - - {line}";
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                _ = this.client.SendAsync(bytes, bytes.Length, Host, Port);
            }
            catch
            {
            }
        }
    }
}
