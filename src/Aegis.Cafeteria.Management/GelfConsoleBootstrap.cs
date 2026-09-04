namespace Aegis.Cafeteria.Management;

using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

internal static class GelfConsoleBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Console.SetOut(new GelfForwardingTextWriter(Console.Out, "Aegis.Cafeteria.Management", 6));
        Console.SetError(new GelfForwardingTextWriter(Console.Error, "Aegis.Cafeteria.Management", 3));
    }

    private sealed class GelfForwardingTextWriter : TextWriter
    {
        private const string Host = "10.10.0.3";
        private const int Port = 1699;
        private readonly TextWriter inner;
        private readonly string applicationName;
        private readonly int level;
        private readonly StringBuilder buffer = new();
        private readonly object sync = new();
        private readonly UdpClient client = new();

        public GelfForwardingTextWriter(TextWriter inner, string applicationName, int level)
        {
            this.inner = inner;
            this.applicationName = applicationName;
            this.level = level;
        }

        public override Encoding Encoding => this.inner.Encoding;

        public override void Write(char value)
        {
            this.inner.Write(value);
            lock (this.sync)
            {
                if (value == '\n')
                {
                    FlushLine();
                }
                else if (value != '\r')
                {
                    this.buffer.Append(value);
                }
            }
        }

        public override void Write(string? value)
        {
            if (value is null)
            {
                return;
            }

            foreach (char character in value)
            {
                Write(character);
            }
        }

        public override void WriteLine(string? value)
        {
            if (value is not null)
            {
                Write(value);
            }

            Write('\n');
        }

        private void FlushLine()
        {
            if (this.buffer.Length == 0)
            {
                return;
            }

            string line = this.buffer.ToString();
            this.buffer.Clear();

            try
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes
                (
                    new Dictionary<string, object?>
                    {
                          ["version"] = "1.1"
                        , ["host"] = Environment.MachineName
                        , ["short_message"] = line
                        , ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
                        , ["level"] = this.level
                        , ["_application"] = this.applicationName
                        , ["_process_id"] = Environment.ProcessId
                    }
                );
                _ = this.client.SendAsync(bytes, bytes.Length, Host, Port);
            }
            catch
            {
            }
        }
    }
}
