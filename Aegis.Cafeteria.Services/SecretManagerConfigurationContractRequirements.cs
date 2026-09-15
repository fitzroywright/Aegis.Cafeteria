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
        OpenBaoOptions openBao = configuration.GetSection("CommonSecrets:OpenBao").Get<OpenBaoOptions>() ?? new();
        BitwardenSecretsManagerOptions bitwarden = configuration.GetSection("CommonSecrets:Bitwarden").Get<BitwardenSecretsManagerOptions>() ?? new();
        BitwardenPasswordManagerOptions passwordManager = configuration.GetSection("CommonSecrets:BitwardenPasswordManager").Get<BitwardenPasswordManagerOptions>() ?? new();

        string[] providerOrder = CommonSecretsPolicy.ResolveProviderOrder(common);
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

        foreach (string provider in providerOrder)
        {
            string normalized = Normalize(provider);
            switch (normalized)
            {
                case "OpenBao":
                    AddOpenBao(requirements, openBao);
                    break;
                case "BitwardenSecretsManager":
                    AddBitwardenSecretsManager(requirements, bitwarden);
                    break;
                case "BitwardenPasswordManager":
                    AddBitwardenPasswordManager(requirements, passwordManager);
                    break;
                case "Environment":
                    AddProviderIdentity(requirements, "environment", "Environment Variables", "Built-in process environment source");
                    break;
                case "Configuration":
                    AddProviderIdentity(requirements, "configuration", "Application Configuration", "IConfiguration fallback source");
                    break;
            }
        }
    }

    private static void AddOpenBao(JsonArray requirements, OpenBaoOptions options)
    {
        AddProviderIdentity(requirements, "openbao", "OpenBao", "Common.Secrets OpenBao provider");
        AddMetadata(requirements, "secrets-openbao-enabled", "OpenBao Enabled", "CommonSecrets:OpenBao:Enabled", Bool(options.Enabled), "Whether the OpenBao provider is enabled.", "Resolved Common.Secrets options");
        AddMetadata(requirements, "secrets-openbao-address", "OpenBao Address", "CommonSecrets:OpenBao:Address", SafeText(options.Address), "OpenBao server endpoint used by the application.", "Resolved Common.Secrets options");
        AddMetadata(requirements, "secrets-openbao-endpoint-scope", "OpenBao Endpoint Scope", null, DescribeEndpointScope(options.Address), "What the application can safely infer about where the configured OpenBao endpoint is hosted.", "Derived from configured endpoint");
        AddMetadata(requirements, "secrets-openbao-mount", "OpenBao Secret Mount", "CommonSecrets:OpenBao:MountPath", options.MountPath, "KV mount used for application secrets.", "Resolved Common.Secrets options");
        AddMetadata(requirements, "secrets-openbao-base-path", "OpenBao Base Path", "CommonSecrets:OpenBao:BasePath", options.BasePath, "Base namespace used by this application in OpenBao.", "Resolved Common.Secrets options");
        AddMetadata(requirements, "secrets-openbao-auth-mount", "OpenBao Authentication Mount", "CommonSecrets:OpenBao:AuthMountPath", options.AuthMountPath, "Authentication mount name only; no role IDs or bootstrap credentials are published.", "Resolved Common.Secrets options");
        AddMetadata(requirements, "secrets-openbao-https", "OpenBao Requires HTTPS", "CommonSecrets:OpenBao:RequireHttps", Bool(options.RequireHttps), "Whether Common.Secrets requires HTTPS for the OpenBao endpoint.", "Resolved Common.Secrets options");
    }

    private static void AddBitwardenSecretsManager(JsonArray requirements, BitwardenSecretsManagerOptions options)
    {
        AddProviderIdentity(requirements, "bitwarden-secrets-manager", "Bitwarden Secrets Manager", "Common.Secrets Bitwarden Secrets Manager provider");
        AddMetadata(requirements, "secrets-bitwarden-enabled", "Bitwarden Secrets Manager Enabled", "CommonSecrets:Bitwarden:Enabled", Bool(options.Enabled), "Whether the Bitwarden Secrets Manager provider is enabled.", "Resolved Common.Secrets options");
        string endpoint = string.IsNullOrWhiteSpace(options.ServerUrl) ? "Bitwarden default service" : options.ServerUrl.Trim();
        AddMetadata(requirements, "secrets-bitwarden-server", "Bitwarden Server", "CommonSecrets:Bitwarden:ServerUrl", endpoint, "Bitwarden service endpoint. A blank configured URL means the provider's default Bitwarden service.", "Resolved Common.Secrets options");
        AddMetadata(requirements, "secrets-bitwarden-endpoint-scope", "Bitwarden Endpoint Scope", null, string.IsNullOrWhiteSpace(options.ServerUrl) ? "Vendor/default service" : DescribeEndpointScope(options.ServerUrl), "What the application can safely infer about where the configured Bitwarden endpoint is hosted.", "Derived from configured endpoint");
    }

    private static void AddBitwardenPasswordManager(JsonArray requirements, BitwardenPasswordManagerOptions options)
    {
        AddProviderIdentity(requirements, "bitwarden-password-manager", "Bitwarden Password Manager", "Common.Secrets Bitwarden Password Manager provider");
        AddMetadata(requirements, "secrets-bitwarden-pm-enabled", "Bitwarden Password Manager Enabled", "CommonSecrets:BitwardenPasswordManager:Enabled", Bool(options.Enabled), "Whether the Bitwarden Password Manager provider is enabled.", "Resolved Common.Secrets options");
        AddMetadata(requirements, "secrets-bitwarden-pm-url", "Bitwarden Password Manager URL", "CommonSecrets:BitwardenPasswordManager:BaseUrl", SafeText(options.BaseUrl), "Local/helper API endpoint used by the provider.", "Resolved Common.Secrets options");
        AddMetadata(requirements, "secrets-bitwarden-pm-endpoint-scope", "Bitwarden Password Manager Endpoint Scope", null, DescribeEndpointScope(options.BaseUrl), "What the application can safely infer about where the configured Password Manager helper endpoint is hosted.", "Derived from configured endpoint");
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

    private static string Normalize(string provider)
        => string.Equals(provider, "Bitwarden", StringComparison.OrdinalIgnoreCase)
            ? "BitwardenSecretsManager"
            : provider.Trim();

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
