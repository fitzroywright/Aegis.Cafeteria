using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Aegis.Cafeteria.Services;

/// <summary>
/// Captures deployment identity facts the process can directly observe.
/// Live health and capacity telemetry (memory availability, free disk, interface status,
/// latency and dependency reachability) belongs to Aegis.Diagnostics and is intentionally
/// excluded from the Configuration contract.
/// </summary>
internal static class DetectedRuntimeEnvironment
{
    public static JsonObject Capture(string hostingEnvironment, string contentRootPath)
    {
        IPGlobalProperties ip = IPGlobalProperties.GetIPGlobalProperties();
        JsonArray interfaces = [];

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(item => item.OperationalStatus == OperationalStatus.Up)
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            IPInterfaceProperties properties;
            try { properties = networkInterface.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }

            JsonArray addresses = [];
            foreach (UnicastIPAddressInformation address in properties.UnicastAddresses
                         .Where(item => item.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                         .Where(item => !IPAddress.IsLoopback(item.Address)))
            {
                addresses.Add(address.Address.ToString());
            }

            JsonArray gateways = [];
            foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses
                         .Where(item => item.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                         .Where(item => !IPAddress.Any.Equals(item.Address) && !IPAddress.IPv6Any.Equals(item.Address)))
            {
                gateways.Add(gateway.Address.ToString());
            }

            if (addresses.Count == 0 && gateways.Count == 0) continue;

            interfaces.Add(new JsonObject
            {
                ["name"] = networkInterface.Name,
                ["type"] = networkInterface.NetworkInterfaceType.ToString(),
                ["addresses"] = addresses,
                ["gateways"] = gateways
            });
        }

        JsonArray storage = [];
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(item => item.IsReady))
        {
            try
            {
                storage.Add(new JsonObject
                {
                    ["name"] = drive.Name,
                    ["driveType"] = drive.DriveType.ToString(),
                    ["format"] = drive.DriveFormat
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return new JsonObject
        {
            ["machineName"] = Environment.MachineName,
            ["hostName"] = Dns.GetHostName(),
            ["dnsDomainName"] = NullIfBlank(ip.DomainName),
            ["hostingEnvironment"] = hostingEnvironment,
            ["operatingSystem"] = RuntimeInformation.OSDescription,
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["frameworkDescription"] = RuntimeInformation.FrameworkDescription,
            ["processorCount"] = Environment.ProcessorCount,
            ["contentRootPath"] = contentRootPath,
            ["networkInterfaces"] = interfaces,
            ["storage"] = storage
        };
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
