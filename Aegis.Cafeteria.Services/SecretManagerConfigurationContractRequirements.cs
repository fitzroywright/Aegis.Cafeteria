using System.Net;
using System.Text.Json.Nodes;
using Common.Secrets;

namespace Aegis.Cafeteria.Services;

/// <summary>
/// Publishes only non-secret metadata that describes how Common.Secrets is configured.
/// Credential material, role identifiers, tokens, secret IDs, file paths containing bootstrap
/// material, and secret values are intentionally outside this contract.
/// </summary>
internal static class SecretManagerConfigurationContractRequirements
{
    public static void Add(JsonArray requirements, IConfiguration configuration)
    {
        CommonSecretsOptions common = configuration.GetSection("CommonSecrets").Get<CommonSecretsOptions>() ?? new();
        IReadOnlyDictionary<string, SecretProviderDefinition> providers =
            SecretProviderConfiguration.ReadProviders(configuration);

        string[] providerOrder = CommonSecretsPolicy.ResolveProviderOrder(common, providers);
        string providerSummary = string.Join(" -> ", providerOrder);

        AddMetadata(requirements,
            "secrets-mode",
            "Secret Manager Mode",
            "CommonSecrets:Mode",
            common.Mode.ToString(),
            "Common.Secrets operating mode for this application.",
            common.ProviderOrder is null ? "Common.Secrets policy default" : "Configured");

        AddMetadata(requirements,
            "secrets-provider-order",
            "Secret Provider Order",
            "CommonSecrets:ProviderOrder",
            providerSummary,
            "Ordered secret providers the application will consult.",
            common.ProviderOrder is null ? "Common.Secrets policy default" : "Configured");

        foreach (string providerName in providerOrder)
        {
            SecretProviderDefinition definition = providers[providerName];
            string normalized = Normalize(definition.Type);

            switch (normalized)
            {
                case "OpenBao":
                    AddOpenBao(
                        requirements,
                        providerName,
                        definition.Settings.Get<OpenBaoOptions>() ?? new OpenBaoOptions());
                    break;
                case "BitwardenSecretsManager":
                    AddBitwardenSecretsManager(
                        requirements,
                        providerName,
                        definition.Settings.Get<BitwardenSecretsManagerOptions>() ?? new BitwardenSecretsManagerOptions());
                    break;
                case "BitwardenPasswordManager":
                    AddBitwardenPasswordManager(
                        requirements,
                        providerName,
                        definition.Settings.Get<BitwardenPasswordManagerOptions>() ?? new BitwardenPasswordManagerOptions());
                    break;
                case "Environment":
                    AddProviderIdentity(requirements, ProviderId(providerName), providerName, "Built-in process environment source");
                    break;
                case "Configuration":
                    AddProviderIdentity(requirements, ProviderId(providerName), providerName, "Application configuration source");
                    break;
            }
        }
    }

    private static void AddOpenBao(JsonArray requirements, string providerName, OpenBaoOptions options)
    {
        string prefix = $"CommonSecrets:Providers:{providerName}:Settings";
        string id = ProviderId(providerName);
        AddProviderIdentity(requirements, id, providerName, "Common.Secrets OpenBao provider instance");
        AddMetadata(requirements, $"secrets-{id}-enabled", $"{providerName} Enabled", $"{prefix}:Enabled", Bool(options.Enabled), "Whether this OpenBao provider instance is enabled.", "Resolved provider instance settings");
        AddMetadata(requirements, $"secrets-{id}-address", $"{providerName} Address", $"{prefix}:Address", SafeText(options.Address), "OpenBao server endpoint used by the application.", "Resolved provider instance settings");
        AddMetadata(requirements, $"secrets-{id}-endpoint-scope", $"{providerName} Endpoint Scope", null, DescribeEndpointScope(options.Address), "What the application can safely infer about where the configured OpenBao endpoint is hosted.", "Derived from configured endpoint");
        AddMetadata(requirements, $"secrets-{id}-mount", $"{providerName} Secret Mount", $"{prefix}:MountPath", options.MountPath, "KV mount used for application secrets.", "Resolved provider instance settings");
        AddMetadata(requirements, $"secrets-{id}-base-path", $"{providerName} Base Path", $"{prefix}:BasePath", options.BasePath, "Base namespace used by this application in OpenBao.", "Resolved provider instance settings");
        AddMetadata(requirements, $"secrets-{id}-auth-mount", $"{providerName} Authentication Mount", $"{prefix}:AuthMountPath", options.AuthMountPath, "Authentication mount name only; no role IDs or bootstrap credentials are published.", "Resolved provider instance settings");
        AddMetadata(requirements, $"secrets-{id}-https", $"{providerName} Requires HTTPS", $"{prefix}:RequireHttps", Bool(options.RequireHttps), "Whether Common.Secrets requires HTTPS for this OpenBao endpoint.", "Resolved provider instance settings");
    }

    private static void AddBitwardenSecretsManager(JsonArray requirements, string providerName, BitwardenSecretsManagerOptions options)
    {
        string prefix = $"CommonSecrets:Providers:{providerName}:Settings";
        string id = ProviderId(providerName);
        AddProviderIdentity(requirements, id, providerName, "Common.Secrets Bitwarden Secrets Manager provider instance");
        AddMetadata(requirements, $"secrets-{id}-enabled", $"{providerName} Enabled", $"{prefix}:Enabled", Bool(options.Enabled), "Whether this Bitwarden Secrets Manager provider instance is enabled.", "Resolved provider instance settings");
        string endpoint = string.IsNullOrWhiteSpace(options.ServerUrl) ? "Bitwarden default service" : options.ServerUrl.Trim();
        AddMetadata(requirements, $"secrets-{id}-server", $"{providerName} Server", $"{prefix}:ServerUrl", endpoint, "Bitwarden service endpoint. A blank configured URL means the provider's default Bitwarden service.", "Resolved provider instance settings");
        AddMetadata(requirements, $"secrets-{id}-endpoint-scope", $"{providerName} Endpoint Scope", null, string.IsNullOrWhiteSpace(options.ServerUrl) ? "Vendor/default service" : DescribeEndpointScope(options.ServerUrl), "What the application can safely infer about where the configured Bitwarden endpoint is hosted.", "Derived from configured endpoint");
    }

    private static void AddBitwardenPasswordManager(JsonArray requirements, string providerName, BitwardenPasswordManagerOptions options)
    {
        string prefix = $"CommonSecrets:Providers:{providerName}:Settings";
        string id = ProviderId(providerName);
        AddProviderIdentity(requirements, id, providerName, "Common.Secrets Bitwarden Password Manager provider instance");
        AddMetadata(requirements, $"secrets-{id}-enabled", $"{providerName} Enabled", $"{prefix}:Enabled", Bool(options.Enabled), "Whether this Bitwarden Password Manager provider instance is enabled.", "Resolved provider instance settings");
        AddMetadata(requirements, $"secrets-{id}-url", $"{providerName} URL", $"{prefix}:BaseUrl", SafeText(options.BaseUrl), "Local/helper API endpoint used by the provider.", "Resolved provider instance settings");
        AddMetadata(requirements, $"secrets-{id}-endpoint-scope", $"{providerName} Endpoint Scope", null, DescribeEndpointScope(options.BaseUrl), "What the application can safely infer about where the configured Password Manager helper endpoint is hosted.", "Derived from configured endpoint");
    }

    private static void AddProviderIdentity(JsonArray requirements, string idSuffix, string name, string description)
        => AddMetadata(requirements, $"secrets-provider-{idSuffix}", $"Secret Provider: {name}", null, name, description, "Active provider order");

    private static void AddMetadata(JsonArray requirements, string id, string displayName, string? configurationKey, string? value, string purpose, string source)
    {
        bool available = !string.IsNullOrWhiteSpace(value);
        requirements.Add(new JsonObject
        {
            ["id"] = id,
            ["displayName"] = displayName,
            ["kind"] = "Configuration",
            ["required"] = false,
            ["purpose"] = purpose,
            ["configurationKey"] = configurationKey,
            ["key"] = configurationKey,
            ["sensitive"] = false,
            ["effectiveSource"] = source,
            ["configurationState"] = available ? "Configured" : "Unresolved",
            ["isConfigured"] = available,
            ["effectiveValueAvailable"] = available,
            ["hasDefault"] = false,
            ["defaultValue"] = null,
            ["safeDisplayValue"] = available ? value : null
        });
    }

    private static string Normalize(string providerType)
        => providerType.Trim();

    private static string ProviderId(string providerName)
        => new string(providerName.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray()).Trim('-');

    private static string Bool(bool value) => value ? "true" : "false";
    private static string? SafeText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string DescribeEndpointScope(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "Not configured";
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out Uri? uri)) return "Configured endpoint; scope cannot be determined";

        string host = uri.Host;
        if (uri.IsLoopback || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return "Local machine";

        if (IPAddress.TryParse(host, out IPAddress? ip))
        {
            if (IsPrivate(ip)) return "Private network";
            return "Public/remote address";
        }

        return "DNS endpoint; hosting locality cannot be determined from configuration alone";
    }

    private static bool IsPrivate(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || bytes[0] == 127;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.Equals(IPAddress.IPv6Loopback);

        return false;
    }
}
