using Microsoft.Extensions.Hosting;

namespace Aegis.Cafeteria.UI.Linux;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        LinuxUiParityPolicy.ConfigureApplicationCulture();
        using IHost host = LinuxCafeteriaHost.Build(args);
        LinuxDesktopRunner.Run(host, args);
    }
}
